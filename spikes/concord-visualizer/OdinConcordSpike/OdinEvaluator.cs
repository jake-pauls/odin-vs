using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace OdinConcordSpike;

// A language evaluator in front of the C++ one (LanguageId C, which is what the Odin PDB reports). Every call is
// chained to the next implementation, and what comes back is adjusted:
//  C: results whose type is an Odin slice `[]T` or dynamic array `[dynamic]T` are replaced by our own result that shows
//     `len`/`cap` and expands to `[len]`, `[cap]` and the elements. The elements come from the C++ evaluator itself
//     (`<name>.data,<len>`), so element display and nested expansion keep working.
//  D1: C scalar type names are shown with their Odin names (`__int64` -> `int`, `__int64 *` -> `^int`). The PDB cannot
//     tell `int` from `i64`, so this is a best guess (see FINDINGS.md). Retyped results forward everything else to the original.
//  D2: Watch expressions the C evaluator rejects are retried as Odin: `len(x)` -> `x.len`, `a[i]` -> `a.data[i]`.
// Anything we cannot parse or read is returned unchanged. Public because the dispatcher instantiates it by name.
public sealed class OdinEvaluator : IDkmLanguageExpressionEvaluator
{
    private const long MaxSaneLength = 100_000_000;
    private const int MaxIndexRewrites = 4;
    private const int LoggedSequenceLimit = 12;
    private const int PerfLogInterval = 50;

    private static readonly Regex m_SliceType = new(@"^\[\](?<elem>.+)$", RegexOptions.Compiled);
    private static readonly Regex m_DynamicType = new(@"^\[dynamic\](?<elem>.+)$", RegexOptions.Compiled);
    private static readonly Regex m_FixedType = new(@"^(?<base>[^\[\]]+)\[(?<n>\d+)\]$", RegexOptions.Compiled);
    private static readonly Regex m_LenCap = new(@"len=(?<len>0x[0-9a-fA-F]+|\d+) cap=(?<cap>0x[0-9a-fA-F]+|\d+)", RegexOptions.Compiled | RegexOptions.RightToLeft);
    private static readonly Regex m_Len = new(@"len=(?<len>0x[0-9a-fA-F]+|\d+)", RegexOptions.Compiled | RegexOptions.RightToLeft);
    private static readonly Regex m_Builtin = new(@"^\s*(?<fn>len|cap)\((?<arg>.+)\)\s*$", RegexOptions.Compiled);

    // CodeView built-ins the Odin PDB uses for Odin scalars; the first Odin name that maps to each is the guess.
    private static readonly Dictionary<string, string> m_ScalarNames = new()
    {
        ["__int64"] = "int",
        ["unsigned __int64"] = "uint",
        ["float"] = "f32",
        ["double"] = "f64",
        ["char32_t"] = "rune",
        ["unsigned char"] = "u8",
        ["char *"] = "cstring",
    };

    // Prefix of the C++ value text of an Odin union with a tag; the prefix is never cut off, the rest may be.
    private static readonly Regex m_Union = new(@"^\{tag=(?<tag>\d+) v\d+=", RegexOptions.Compiled);

    // `matrix[R, C]T`, or `#row_major matrix[R, C]T`: the PDB type name carries the storage order.
    private static readonly Regex m_Matrix = new(@"^(?<rm>#row_major )?matrix\[(?<r>\d+), (?<c>\d+)\](?<elem>.+)$", RegexOptions.Compiled);

    private const int MaxMatrixCells = 64;

    private static readonly Regex m_Signed =new(@"^\s*(?<sign>-?)(?<n>0x[0-9a-fA-F]+|\d+)", RegexOptions.Compiled);

    private static readonly Regex m_Number = new(@"^\s*(0x[0-9a-fA-F]+|\d+)", RegexOptions.Compiled);

    // Odin scalar names back to the PDB's C names (the first Odin name of each pair in m_ScalarNames is the display guess).
    private static readonly Dictionary<string, string> m_OdinToC = new()
    {
        ["int"] = "__int64",
        ["i64"] = "__int64",
        ["uint"] = "unsigned __int64",
        ["u64"] = "unsigned __int64",
        ["uintptr"] = "unsigned __int64",
        ["f32"] = "float",
        ["f64"] = "double",
        ["rune"] = "char32_t",
        ["u8"] = "unsigned char",
        ["i32"] = "int",
        ["u32"] = "unsigned int",
        ["i16"] = "short",
        ["u16"] = "unsigned short",
        ["i8"] = "signed char",
        ["cstring"] = "char *",
    };

    private const ulong MaxMapCapacity = 1UL << 20;

    // The sample's module; the context operator in a cast spelling needs it (experiment only).
    private const string ModuleName = "ConcordSample.exe";

    private static int m_VariantLogCount;
    private static int m_SequenceLogCount;
    private static int m_PerfCalls;
    private static long m_PerfItems;
    private static long m_PerfOurTicks;
    private static long m_PerfDownstreamTicks;

    public OdinEvaluator()
    {
        SpikeLog.Write("OdinEvaluator constructed");
    }

    public void GetFrameLocals(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine)
    {
        if (!OdinModules.IsOdinFrame(stackFrame))
        {
            inspectionContext.GetFrameLocals(workList, stackFrame, completionRoutine);
            return;
        }

        inspectionContext.GetFrameLocals(workList, stackFrame, result =>
        {
            if (result.ErrorCode != 0 || result.EnumContext is null)
            {
                completionRoutine(result);
                return;
            }

            DkmEvaluationResultEnumContext wrapped = DkmEvaluationResultEnumContext.Create(
                result.EnumContext.Count, stackFrame, inspectionContext, new WrappedEnum(result.EnumContext));
            completionRoutine(new DkmGetFrameLocalsAsyncResult(wrapped));
        });
    }

    public void GetFrameArguments(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmStackWalkFrame frame,
        DkmCompletionRoutine<DkmGetFrameArgumentsAsyncResult> completionRoutine)
    {
        if (!OdinModules.IsOdinFrame(frame))
        {
            inspectionContext.GetFrameArguments(workList, frame, completionRoutine);
            return;
        }

        inspectionContext.GetFrameArguments(workList, frame, result =>
        {
            if (result.ErrorCode != 0 || result.Arguments is null)
            {
                completionRoutine(result);
                return;
            }

            TransformAllAsync(result.Arguments, inspectionContext, workList, transformed =>
                completionRoutine(new DkmGetFrameArgumentsAsyncResult(transformed)));
        });
    }

    public void EvaluateExpression(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression expression,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        // An attempt we issued ourselves: just hand it to the next evaluator, no rewriting (see EvaluateWithRewrites).
        if (expression.GetDataItem<RetryMarker>() is not null || !OdinModules.IsOdinFrame(stackFrame))
        {
            inspectionContext.EvaluateExpression(workList, expression, stackFrame, completionRoutine);
            return;
        }

        string text = expression.Text;
        Match builtin = m_Builtin.Match(text);
        DkmLanguageExpression first = expression;
        if (builtin.Success)
        {
            text = $"{builtin.Groups["arg"].Value.Trim()}.{builtin.Groups["fn"].Value}";
            first = DkmLanguageExpression.Create(expression.Language, expression.CompilationFlags, text, null);
        }

        EvaluateWithRewrites(inspectionContext, workList, expression, first, text, stackFrame, completionRoutine);
    }

    // Evaluates `first`; if the C evaluator rejects it and the text has `[`, Odin indexing is tried by inserting `.data`
    // before some of the brackets (Odin indexes the slice, C indexes a pointer). Which brackets belong to slices is not
    // known without types, so every subset is tried (at most 2^MaxIndexRewrites) in ONE flat batch issued from the first
    // failure's callback, and the first success in subset order wins. Chaining the retries (each started from the previous
    // one's callback) hung at the third level (`things[0].tags[1]`); a thread-pool workaround crashed devenv or hung too,
    // so nothing here nests deeper than two calls and no thread is created. Attempts carry RetryMarker so they are not rewritten again.
    private void EvaluateWithRewrites(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression original,
        DkmLanguageExpression first,
        string firstText,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        inspectionContext.EvaluateExpression(workList, first, stackFrame, firstResult =>
        {
            if (!IsFailure(firstResult))
            {
                TransformAllAsync([firstResult.ResultObject!], inspectionContext, workList, transformed =>
                    completionRoutine(new DkmEvaluateExpressionAsyncResult(transformed[0])));
                return;
            }

            List<int> brackets = [];
            for (int i = 0; i < firstText.Length && brackets.Count < MaxIndexRewrites; i++)
            {
                if (firstText[i] == '[')
                {
                    brackets.Add(i);
                }
            }

            if (brackets.Count == 0)
            {
                completionRoutine(firstResult);
                return;
            }

            // Subsets in order of size, then value, so the fewest rewrites that work win.
            List<int> masks = [];
            for (int mask = 1; mask < (1 << brackets.Count); mask++)
            {
                masks.Add(mask);
            }

            masks.Sort((x, y) =>
            {
                int byCount = PopCount(x).CompareTo(PopCount(y));
                return byCount != 0 ? byCount : x.CompareTo(y);
            });

            var texts = new string[masks.Count];
            var results = new DkmEvaluateExpressionAsyncResult?[masks.Count];
            int pending = masks.Count;
            for (int k = 0; k < masks.Count; k++)
            {
                int slot = k;
                string rewritten = firstText;
                for (int b = brackets.Count - 1; b >= 0; b--)
                {
                    if ((masks[k] & (1 << b)) != 0)
                    {
                        rewritten = rewritten.Insert(brackets[b], ".data");
                    }
                }

                texts[slot] = rewritten;
                DkmLanguageExpression attempt = DkmLanguageExpression.Create(original.Language, original.CompilationFlags, rewritten, new RetryMarker());
                inspectionContext.EvaluateExpression(workList, attempt, stackFrame, result =>
                {
                    results[slot] = result;
                    if (Interlocked.Decrement(ref pending) != 0)
                    {
                        return;
                    }

                    for (int i = 0; i < results.Length; i++)
                    {
                        if (!IsFailure(results[i]!.Value))
                        {
                            SpikeLog.Write($"expression '{original.Text}' evaluated as '{texts[i]}'");
                            // Already two calls deep: no further evaluation here (see above), so a cut-off header is not resolved.
                            completionRoutine(new DkmEvaluateExpressionAsyncResult(Transform(results[i]!.Value.ResultObject!)));
                            return;
                        }
                    }

                    completionRoutine(firstResult);
                });
            }
        });
    }

    private static bool IsFailure(DkmEvaluateExpressionAsyncResult result)
    {
        return result.ErrorCode != 0 || result.ResultObject is null || result.ResultObject is DkmFailedEvaluationResult;
    }

    private static int PopCount(int value)
    {
        int count = 0;
        for (; value != 0; value &= value - 1)
        {
            count++;
        }

        return count;
    }
    public void GetChildren(
        DkmEvaluationResult result,
        DkmWorkList workList,
        int initialRequestSize,
        DkmInspectionContext inspectionContext,
        DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
    {
        if (result.GetDataItem<SequenceData>() is SequenceData sequence)
        {
            GetSequenceChildren((DkmSuccessEvaluationResult)result, sequence, workList, initialRequestSize, inspectionContext, completionRoutine);
            return;
        }

        if (result.GetDataItem<MatrixData>() is MatrixData matrix)
        {
            GetMatrixChildren((DkmSuccessEvaluationResult)result, matrix, inspectionContext, completionRoutine);
            return;
        }

        if (result.GetDataItem<MapData>() is MapData map)
        {
            GetMapChildren((DkmSuccessEvaluationResult)result, map, workList, inspectionContext, completionRoutine);
            return;
        }

        if (result.GetDataItem<RetypeData>() is null && !OdinModules.IsOdinFrame(result.StackFrame))
        {
            result.GetChildren(workList, initialRequestSize, inspectionContext, completionRoutine);
            return;
        }

        DkmEvaluationResult target = result.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : result;
        target.GetChildren(workList, initialRequestSize, inspectionContext, children =>
        {
            if (children.ErrorCode != 0 || children.EnumContext is null)
            {
                completionRoutine(children);
                return;
            }

            DkmEvaluationResultEnumContext wrapped = DkmEvaluationResultEnumContext.Create(
                children.EnumContext.Count, result.StackFrame, inspectionContext, new WrappedEnum(children.EnumContext));
            TransformAllAsync(children.InitialChildren, inspectionContext, workList, transformed =>
                completionRoutine(new DkmGetChildrenAsyncResult(transformed, wrapped)));
        });
    }

    public void GetItems(
        DkmEvaluationResultEnumContext enumContext,
        DkmWorkList workList,
        int startIndex,
        int count,
        DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
    {
        // Data items are looked up by exact type, so each enum kind is its own lookup.
        if (enumContext.GetDataItem<WrappedEnum>() is WrappedEnum wrapped)
        {
            long started = Stopwatch.GetTimestamp();
            wrapped.Inner.GetItems(workList, startIndex, count, items =>
            {
                Interlocked.Add(ref m_PerfDownstreamTicks, Stopwatch.GetTimestamp() - started);
                if (items.ErrorCode != 0 || items.Items is null)
                {
                    completionRoutine(items);
                    return;
                }

                TransformAllAsync(items.Items, enumContext.InspectionContext, workList, transformed =>
                    completionRoutine(new DkmEvaluationEnumAsyncResult(transformed)));
            });
        }
        else if (enumContext.GetDataItem<SequenceEnum>() is SequenceEnum sequence)
        {
            sequence.GetItems(workList, startIndex, count, completionRoutine);
        }
        else if (enumContext.GetDataItem<MapEnum>() is MapEnum mapEnum)
        {
            mapEnum.GetItems(this, workList, startIndex, count, completionRoutine);
        }
        else
        {
            enumContext.GetItems(workList, startIndex, count, completionRoutine);
        }
    }

    public void SetValueAsString(DkmEvaluationResult result, string value, int timeout, out string errorText)
    {
        if (result.GetDataItem<SequenceData>() is not null || result.GetDataItem<MapData>() is not null || result.GetDataItem<MatrixData>() is not null)
        {
            errorText = "Odin slices, dynamic arrays, maps and matrices cannot be assigned from the debugger window.";
            return;
        }

        DkmEvaluationResult target = result.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : result;
        string? chainedError;
        target.SetValueAsString(value, timeout, out chainedError);
        errorText = chainedError!;
    }

    public string GetUnderlyingString(DkmEvaluationResult result)
    {
        if (result.GetDataItem<SequenceData>() is not null || result.GetDataItem<MapData>() is not null || result.GetDataItem<MatrixData>() is not null)
        {
            return string.Empty;
        }

        DkmEvaluationResult target = result.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : result;
        return target.GetUnderlyingString()!;
    }

    // The C++ value text is cut off (`len=...`) when a result is long, so a missing len/cap is asked for with an
    // expression first; everything else is transformed as before. `headers` maps a result to {len, cap or -1}.
    private void TransformAllAsync(DkmEvaluationResult[] results, DkmInspectionContext inspectionContext, DkmWorkList workList, Action<DkmEvaluationResult[]> done)
    {
        var expressions = new List<string>();
        var wanted = new List<(int Index, bool Dynamic, int First)>();
        var unions = new List<(int Index, int Expression)>();
        var matrices = new List<(int Index, int First, int Rows, int Columns)>();
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is not DkmSuccessEvaluationResult success || success.Type is null)
            {
                continue;
            }

            // A matrix: every element is evaluated (`data[c][r]` column-major, `data[r][c]` row-major) so the rows can be shown.
            Match matrix = m_Matrix.Match(success.Type);
            if (matrix.Success)
            {
                int rows = int.Parse(matrix.Groups["r"].Value);
                int columns = int.Parse(matrix.Groups["c"].Value);
                if (rows * columns is > 0 and <= MaxMatrixCells)
                {
                    matrices.Add((i, expressions.Count, rows, columns));
                    bool rowMajor = matrix.Groups["rm"].Success;
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < columns; c++)
                        {
                            expressions.Add(rowMajor ? $"{success.FullName}.data[{r}][{c}]" : $"{success.FullName}.data[{c}][{r}]");
                        }
                    }
                }

                continue;
            }

            // A union: the active variant `v<tag>` is evaluated so it can be shown alone (tag 0 is nil, needs no evaluation).
            Match union = m_Union.Match(success.Value);
            if (union.Success)
            {
                if (union.Groups["tag"].Value != "0")
                {
                    unions.Add((i, expressions.Count));
                    expressions.Add($"{success.FullName}.v{union.Groups["tag"].Value}");
                }

                continue;
            }

            bool dynamic = m_DynamicType.IsMatch(success.Type);
            bool isHeaderType = dynamic || m_SliceType.IsMatch(success.Type) || TryParseMapType(success.Type, out _, out _);
            if (!isHeaderType || (dynamic ? m_LenCap.IsMatch(success.Value) : m_Len.IsMatch(success.Value)))
            {
                continue;
            }

            wanted.Add((i, dynamic, expressions.Count));
            expressions.Add($"{success.FullName}.len");
            if (dynamic)
            {
                expressions.Add($"{success.FullName}.cap");
            }
        }

        if (expressions.Count == 0)
        {
            done(TransformAll(results));
            return;
        }

        int frameIndex = wanted.Count > 0 ? wanted[0].Index : unions.Count > 0 ? unions[0].Index : matrices[0].Index;
        EvalBatch(inspectionContext, workList, results[frameIndex].StackFrame, expressions.ToArray(), values =>
        {
            try
            {
                var matrixRows = new Dictionary<DkmEvaluationResult, string[]>();
                foreach ((int index, int first, int rows, int columns) in matrices)
                {
                    var cells = new string[rows * columns];
                    bool complete = true;
                    for (int k = 0; k < cells.Length; k++)
                    {
                        DkmSuccessEvaluationResult? cell = values[first + k];
                        if (cell is null)
                        {
                            complete = false;
                            break;
                        }

                        cells[k] = TrimFloat((Transform(cell) is DkmSuccessEvaluationResult shown ? shown.Value : cell.Value) ?? string.Empty);
                    }

                    if (complete)
                    {
                        matrixRows[results[index]] = cells;
                    }
                    else
                    {
                        SpikeLog.Write($"matrix {results[index].FullName}: elements failed to evaluate, shown natively");
                    }
                }

                var variants = new Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>();
                foreach ((int index, int expression) in unions)
                {
                    if (values[expression] is DkmSuccessEvaluationResult variant)
                    {
                        variants[results[index]] = variant;
                    }
                    else
                    {
                        SpikeLog.Write($"union variant of {results[index].FullName} failed to evaluate");
                    }
                }

                var headers = new Dictionary<DkmEvaluationResult, long[]>();
                foreach ((int index, bool dynamic, int first) in wanted)
                {
                    DkmSuccessEvaluationResult? len = values[first];
                    DkmSuccessEvaluationResult? cap = dynamic ? values[first + 1] : null;
                    if (len is null || (dynamic && cap is null))
                    {
                        SpikeLog.Write($"header expressions for {results[index].FullName} failed");
                        continue;
                    }

                    // A garbage len such as -1 does not parse as unsigned: skip that one result, keep the rest of the batch
                    // (an exception here used to drop the union variants of every result in it).
                    if (!TryParseSigned(len.Value, out long lenValue) || (dynamic && !TryParseSigned(cap!.Value, out _)))
                    {
                        SpikeLog.Write($"header of {results[index].FullName} is not a plain number ('{len.Value}'), shown natively");
                        continue;
                    }

                    TryParseSigned(dynamic ? cap!.Value : "0", out long capValue);
                    headers[results[index]] = [lenValue, dynamic ? capValue : -1];
                }

                done(TransformAll(results, headers, variants, matrixRows));
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"resolving headers failed: {ex}");
                done(TransformAll(results));
            }
        });
    }

    private DkmEvaluationResult[] TransformAll(
        DkmEvaluationResult[] results,
        Dictionary<DkmEvaluationResult, long[]>? headers = null,
        Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>? variants = null,
        Dictionary<DkmEvaluationResult, string[]>? matrices = null)
    {
        long started = Stopwatch.GetTimestamp();
        var transformed = new DkmEvaluationResult[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            transformed[i] = Transform(results[i], headers, variants, matrices);
        }

        Interlocked.Add(ref m_PerfOurTicks, Stopwatch.GetTimestamp() - started);
        Interlocked.Add(ref m_PerfItems, results.Length);
        int calls = Interlocked.Increment(ref m_PerfCalls);
        if (calls % PerfLogInterval == 0)
        {
            double ourMs = m_PerfOurTicks * 1000.0 / Stopwatch.Frequency;
            double downstreamMs = m_PerfDownstreamTicks * 1000.0 / Stopwatch.Frequency;
            SpikeLog.Write($"perf: {calls} TransformAll calls, {m_PerfItems} items, our transform {ourMs:F2} ms total ({ourMs / calls * 1000:F0} us/call); downstream GetItems (C++ evaluator) {downstreamMs:F2} ms total");
        }

        return transformed;
    }

    private DkmEvaluationResult Transform(
        DkmEvaluationResult result,
        Dictionary<DkmEvaluationResult, long[]>? headers = null,
        Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>? variants = null,
        Dictionary<DkmEvaluationResult, string[]>? matrices = null)
    {
        try
        {
            if (result is not DkmSuccessEvaluationResult success || success.Type is null)
            {
                return result;
            }

            Match matrix = m_Matrix.Match(success.Type);
            if (matrix.Success)
            {
                return matrices is not null && matrices.TryGetValue(success, out string[]? cells)
                    ? TransformMatrix(success, matrix, cells)
                    : result;
            }

            Match union = m_Union.Match(success.Value);
            if (union.Success)
            {
                return TransformUnion(success, union.Groups["tag"].Value, variants) ?? result;
            }

            if (TryParseMapType(success.Type, out string mapKey, out string mapValue))
            {
                return TransformMap(success, mapKey, mapValue, headers) ?? result;
            }

            bool dynamic = false;
            string? elementType = null;
            Match slice = m_SliceType.Match(success.Type);
            if (slice.Success)
            {
                elementType = slice.Groups["elem"].Value;
            }
            else
            {
                Match dyn = m_DynamicType.Match(success.Type);
                if (dyn.Success)
                {
                    dynamic = true;
                    elementType = dyn.Groups["elem"].Value;
                }
            }

            if (elementType is not null)
            {
                return TransformSequence(success, dynamic, headers) ?? result;
            }

            string? odinType = MapType(success.Type);
            return odinType is null ? result : Retype(success, odinType);
        }
        catch (Exception ex)
        {
            SpikeLog.Write($"transform failed for {result.FullName}: {ex}");
            return result;
        }
    }

    private static string TrimFloat(string text)
    {
        string trimmed = text.Trim();
        return Regex.IsMatch(trimmed, @"^-?\d+\.\d+$") ? trimmed.TrimEnd('0').TrimEnd('.') : trimmed;
    }

    // Shows a matrix the way Odin writes it (`matrix[1, 2, 3; 4, 5, 6]`, one row per `;`) whatever the storage order, and says
    // which order the data is stored in: Odin's default is column-major, `#row_major` opts out. `cells` are row by row.
    private static DkmEvaluationResult TransformMatrix(DkmSuccessEvaluationResult success, Match matrix, string[] cells)
    {
        int rows = int.Parse(matrix.Groups["r"].Value);
        int columns = int.Parse(matrix.Groups["c"].Value);
        bool rowMajor = matrix.Groups["rm"].Success;
        var rowTexts = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new string[columns];
            Array.Copy(cells, r * columns, row, 0, columns);
            rowTexts[r] = string.Join(", ", row);
        }

        string order = rowMajor ? "row-major" : "column-major";
        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            DkmEvaluationResultFlags.Expandable | DkmEvaluationResultFlags.ReadOnly,
            $"matrix[{string.Join("; ", rowTexts)}] ({order})",
            null,
            success.Type,
            success.Category,
            success.Access,
            success.StorageType,
            success.TypeModifierFlags,
            success.Address,
            null,
            null,
            new MatrixData(rowTexts, columns, matrix.Groups["elem"].Value, order));
    }

    private void GetMatrixChildren(
        DkmSuccessEvaluationResult result,
        MatrixData matrix,
        DkmInspectionContext inspectionContext,
        DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
    {
        var rows = new List<DkmEvaluationResult>();
        for (int r = 0; r < matrix.Rows.Count; r++)
        {
            rows.Add(DkmSuccessEvaluationResult.Create(
                inspectionContext,
                result.StackFrame,
                $"[{r}]",
                null,
                DkmEvaluationResultFlags.ReadOnly,
                $"{{{matrix.Rows[r]}}}",
                null,
                $"[{matrix.Columns}]{matrix.ElementType}",
                DkmEvaluationResultCategory.Data,
                DkmEvaluationResultAccessType.None,
                DkmEvaluationResultStorageType.None,
                DkmEvaluationResultTypeModifierFlags.None,
                null,
                null,
                null,
                null));
        }

        rows.Add(DkmSuccessEvaluationResult.Create(
            inspectionContext,
            result.StackFrame,
            "[storage]",
            null,
            DkmEvaluationResultFlags.ReadOnly,
            matrix.Order,
            null,
            "string",
            DkmEvaluationResultCategory.Data,
            DkmEvaluationResultAccessType.None,
            DkmEvaluationResultStorageType.None,
            DkmEvaluationResultTypeModifierFlags.None,
            null,
            null,
            null,
            null));

        DkmEvaluationResult[] all = rows.ToArray();
        completionRoutine(new DkmGetChildrenAsyncResult(
            all,
            DkmEvaluationResultEnumContext.Create(all.Length, result.StackFrame, inspectionContext, new SequenceEnum(rows, null, r => r))));
    }

    // Odin unions reach us as structs `{tag=N v1=... v2=...}` (tag 0 = nil, variant k is member `v<k>`; no tag for a
    // single-pointer union, which has just `v0` and stays as the C++ evaluator shows it). Shows only the active variant;
    // children, editing and strings come from the evaluated variant.
    private DkmEvaluationResult? TransformUnion(DkmSuccessEvaluationResult success, string tag, Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>? variants)
    {
        if (tag == "0")
        {
            return DkmSuccessEvaluationResult.Create(
                success.InspectionContext,
                success.StackFrame,
                success.Name,
                success.FullName,
                DkmEvaluationResultFlags.ReadOnly,
                "nil",
                null,
                success.Type,
                success.Category,
                success.Access,
                success.StorageType,
                success.TypeModifierFlags,
                success.Address,
                null,
                null,
                new RetypeData(success));
        }

        if (variants is null || !variants.TryGetValue(success, out DkmSuccessEvaluationResult? variant))
        {
            return null;
        }

        DkmSuccessEvaluationResult shown = Transform(variant) as DkmSuccessEvaluationResult ?? variant;
        bool expandable = (variant.Flags & DkmEvaluationResultFlags.Expandable) != 0;
        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            variant.Flags,
            expandable ? $"{shown.Type} {shown.Value}" : shown.Value,
            variant.EditableValue,
            success.Type,
            success.Category,
            success.Access,
            success.StorageType,
            success.TypeModifierFlags,
            success.Address,
            variant.CustomUIVisualizers,
            variant.ExternalModules,
            new RetypeData(variant));
    }

    private static DkmEvaluationResult? TransformSequence(DkmSuccessEvaluationResult success, bool dynamic, Dictionary<DkmEvaluationResult, long[]>? headers)
    {
        long length;
        long? capacity;
        string how;
        if (headers is not null && headers.TryGetValue(success, out long[]? resolved))
        {
            length = resolved[0];
            capacity = dynamic ? resolved[1] : null;
            how = "expression";
            if (!IsSane(length, capacity))
            {
                return null;
            }
        }
        else if (!TryReadHeader(success, dynamic, out length, out capacity, out how))
        {
            return null;
        }

        string value = dynamic ? $"{{len={length} cap={capacity}}}" : $"{{len={length}}}";
        if (Interlocked.Increment(ref m_SequenceLogCount) <= LoggedSequenceLimit)
        {
            SpikeLog.Write($"sequence {success.FullName} : {success.Type} -> {value} (header via {how}; further sequences are not logged)");
        }

        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            DkmEvaluationResultFlags.Expandable | DkmEvaluationResultFlags.ReadOnly,
            value,
            null,
            success.Type,
            success.Category,
            success.Access,
            success.StorageType,
            success.TypeModifierFlags,
            success.Address,
            null,
            null,
            new SequenceData(length, capacity, dynamic));
    }

    private static DkmEvaluationResult Retype(DkmSuccessEvaluationResult success, string odinType)
    {
        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            success.Flags,
            success.Value,
            success.EditableValue,
            odinType,
            success.Category,
            success.Access,
            success.StorageType,
            success.TypeModifierFlags,
            success.Address,
            success.CustomUIVisualizers,
            success.ExternalModules,
            new RetypeData(success));
    }

    private static string? MapType(string type)
    {
        if (m_ScalarNames.TryGetValue(type, out string? scalar))
        {
            return scalar;
        }

        if (type.EndsWith(" *", StringComparison.Ordinal))
        {
            string? pointee = MapType(type.Substring(0, type.Length - 2));
            return pointee is null ? null : "^" + pointee;
        }

        Match fixedArray = m_FixedType.Match(type);
        if (fixedArray.Success)
        {
            string? element = MapType(fixedArray.Groups["base"].Value);
            return element is null ? null : $"[{fixedArray.Groups["n"].Value}]{element}";
        }

        return null;
    }

    // Reads len (and cap) from the target's memory when the result has an address, else from the C++ value text.
    private static bool TryReadHeader(DkmSuccessEvaluationResult result, bool dynamic, out long length, out long? capacity, out string how)
    {
        length = 0;
        capacity = null;
        how = string.Empty;

        ulong address = result.Address?.Value ?? 0;
        if (address != 0)
        {
            try
            {
                byte[] buffer = new byte[dynamic ? 24 : 16];
                result.RuntimeInstance.Process.ReadMemory(address, DkmReadMemoryFlags.None, buffer);
                length = BitConverter.ToInt64(buffer, 8);
                capacity = dynamic ? BitConverter.ToInt64(buffer, 16) : null;
                how = "memory";
                return IsSane(length, capacity);
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"memory read for {result.FullName} failed, using text: {ex.Message}");
            }
        }

        Match match = dynamic ? m_LenCap.Match(result.Value) : m_Len.Match(result.Value);
        if (!match.Success)
        {
            return false;
        }

        length = ParseNumber(match.Groups["len"].Value);
        capacity = dynamic ? ParseNumber(match.Groups["cap"].Value) : null;
        how = "text";
        return IsSane(length, capacity);
    }

    private static bool IsSane(long length, long? capacity)
    {
        return length is >= 0 and <= MaxSaneLength && (capacity is null || capacity is >= 0 and <= MaxSaneLength * 4);
    }

    private static long ParseNumber(string text)
    {
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(text.Substring(2), 16) : long.Parse(text);
    }

    private void GetSequenceChildren(
        DkmSuccessEvaluationResult result,
        SequenceData sequence,
        DkmWorkList workList,
        int initialRequestSize,
        DkmInspectionContext inspectionContext,
        DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
    {
        var pseudo = new List<DkmEvaluationResult>
        {
            Pseudo(result, inspectionContext, "[len]", "len", sequence.Length.ToString()),
        };
        if (sequence.Capacity is long capacity)
        {
            pseudo.Add(Pseudo(result, inspectionContext, "[cap]", "cap", capacity.ToString()));
        }

        void OnlyPseudo()
        {
            DkmEvaluationResult[] only = pseudo.ToArray();
            completionRoutine(new DkmGetChildrenAsyncResult(
                only,
                DkmEvaluationResultEnumContext.Create(only.Length, result.StackFrame, inspectionContext, new SequenceEnum(pseudo, null, r => TransformAll(r)))));
        }

        if (sequence.Length == 0)
        {
            OnlyPseudo();
            return;
        }

        // The C++ evaluator's own array view: `name.data,N` expands to [0]..[N-1] with full native element display.
        string expression = $"{result.FullName}.data,{sequence.Length}";
        DkmLanguageExpression elements = DkmLanguageExpression.Create(inspectionContext.Language, DkmEvaluationFlags.TreatAsExpression, expression, null);
        inspectionContext.EvaluateExpression(workList, elements, result.StackFrame, evaluated =>
        {
            if (evaluated.ErrorCode != 0 || evaluated.ResultObject is not DkmSuccessEvaluationResult array)
            {
                SpikeLog.Write($"element view '{expression}' failed: {Describe(evaluated.ResultObject)}");
                OnlyPseudo();
                return;
            }

            // The array view may itself have been retyped by us (pointer to ^T); expand the underlying C++ result.
            DkmEvaluationResult expandable = array.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : array;
            expandable.GetChildren(workList, initialRequestSize, inspectionContext, children =>
            {
                if (children.ErrorCode != 0 || children.EnumContext is null)
                {
                    SpikeLog.Write($"element children of '{expression}' failed: error={children.ErrorCode}");
                    OnlyPseudo();
                    return;
                }

                var initial = new List<DkmEvaluationResult>(pseudo);
                initial.AddRange(TransformAll(children.InitialChildren));
                completionRoutine(new DkmGetChildrenAsyncResult(
                    initial.ToArray(),
                    DkmEvaluationResultEnumContext.Create(
                        pseudo.Count + children.EnumContext.Count,
                        result.StackFrame,
                        inspectionContext,
                        new SequenceEnum(pseudo, children.EnumContext, r => TransformAll(r)))));
            });
        });
    }

    private static DkmEvaluationResult Pseudo(DkmSuccessEvaluationResult parent, DkmInspectionContext inspectionContext, string name, string member, string value)
    {
        return DkmSuccessEvaluationResult.Create(
            inspectionContext,
            parent.StackFrame,
            name,
            $"{parent.FullName}.{member}",
            DkmEvaluationResultFlags.ReadOnly,
            value,
            null,
            "int",
            DkmEvaluationResultCategory.Data,
            DkmEvaluationResultAccessType.None,
            DkmEvaluationResultStorageType.None,
            DkmEvaluationResultTypeModifierFlags.None,
            null,
            null,
            null,
            null);
    }

    // `map[K]V`: the PDB gives only the type name, so keys and values are read the way Odin's runtime lays them out
    // (base/dynamic_map_internal.odin): data = pointer | log2(capacity) in the low 6 bits; keys, values, then hashes follow
    // each other as arrays of 64-byte cells; a slot is used when its hash is non-zero and its top bit is clear.
    private static bool TryParseMapType(string type, out string key, out string value)
    {
        key = value = string.Empty;
        if (!type.StartsWith("map[", StringComparison.Ordinal))
        {
            return false;
        }

        int depth = 0;
        for (int i = 3; i < type.Length; i++)
        {
            if (type[i] == '[')
            {
                depth++;
            }
            else if (type[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    key = type.Substring(4, i - 4);
                    value = type.Substring(i + 1);
                    return key.Length > 0 && value.Length > 0;
                }
            }
        }

        return false;
    }

    // Odin type name back to the C name the native evaluator understands (composites already share the PDB's spelling).
    private static string ToCType(string odin)
    {
        if (odin.StartsWith("^", StringComparison.Ordinal))
        {
            return ToCType(odin.Substring(1)) + " *";
        }

        return m_OdinToC.TryGetValue(odin, out string? c) ? c : odin;
    }

    // `*(T*)0xaddr` for the C++ evaluator. Odin names like `main::Thing` do not cast (observed: `main::Pair`/`main::Thing` fail,
    // `string` and `__int64` work); `main` is also a function, so the qualifier may resolve as a scope inside it. Experiment: try
    // other spellings and log which one the evaluator accepts.
    private static IEnumerable<string> CastVariants(string cType, ulong address)
    {
        string at = $"0x{address:x}";
        yield return $"*({cType}*){at}";
        if (cType.IndexOf("::", StringComparison.Ordinal) < 0)
        {
            yield break;
        }

        yield return $"*(::{cType}*){at}";
        yield return $"*(struct {cType}*){at}";
        yield return $"*(`{cType}`*){at}";
        yield return $"*({{,,{ModuleName}}}{cType}*){at}";
        yield return $"*({cType.Substring(cType.LastIndexOf("::", StringComparison.Ordinal) + 2)}*){at}";
    }

    private static ulong CellSize(ulong size)
    {
        return size > 0 && size < 64 ? 64 : Math.Max(64, (size + 63) / 64 * 64);
    }

    // Where the array after a cell array starts: Odin indexes `capacity` as an element (map_cell_index_dynamic), so a partly
    // filled last cell is skipped only up to its used part (12-byte keys, capacity 8: 100 bytes, not two whole cells).
    private static ulong CellArrayBytes(ulong capacity, ulong size)
    {
        return ElementOffset(capacity, size);
    }

    private static ulong ElementOffset(ulong index, ulong size)
    {
        if (size > 0 && size < 64)
        {
            ulong perCell = 64 / size;
            return index / perCell * 64 + index % perCell * size;
        }

        return index * CellSize(size);
    }

    private static bool TryParseSigned(string? text, out long value)
    {
        value = 0;
        Match match = m_Signed.Match(text ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        string number = match.Groups["n"].Value;
        bool negative = match.Groups["sign"].Value == "-";
        ulong parsed;
        if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            parsed = Convert.ToUInt64(number.Substring(2), 16);
        }
        else if (!ulong.TryParse(number, out parsed))
        {
            return false;
        }

        value = negative ? -(long)parsed : (long)parsed;
        return true;
    }

    private static ulong ParseUnsigned(string? text)
    {
        Match match = m_Number.Match(text ?? string.Empty);
        if (!match.Success)
        {
            throw new FormatException($"no number in '{text}'");
        }

        string number = match.Value;
        return number.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt64(number.Substring(2), 16) : ulong.Parse(number);
    }

    private static DkmEvaluationResult? TransformMap(DkmSuccessEvaluationResult success, string key, string value, Dictionary<DkmEvaluationResult, long[]>? headers)
    {
        long length;
        Match match = m_Len.Match(success.Value);
        if (match.Success)
        {
            length = ParseNumber(match.Groups["len"].Value);
        }
        else if (headers is not null && headers.TryGetValue(success, out long[]? resolved))
        {
            length = resolved[0];
        }
        else
        {
            return null;
        }

        if (!IsSane(length, null))
        {
            return null;
        }

        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            DkmEvaluationResultFlags.Expandable | DkmEvaluationResultFlags.ReadOnly,
            $"{{len={length}}}",
            null,
            success.Type,
            success.Category,
            success.Access,
            success.StorageType,
            success.TypeModifierFlags,
            success.Address,
            null,
            null,
            new MapData(key, value, length));
    }

    // Evaluates all expressions with the C++ evaluator at once (never chained: see EvaluateWithRewrites) and calls `done`
    // from the last completion with the successful results (null where one failed).
    private static void EvalBatch(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmStackWalkFrame stackFrame,
        string[] expressions,
        Action<DkmSuccessEvaluationResult?[]> done)
    {
        var results = new DkmSuccessEvaluationResult?[expressions.Length];
        if (expressions.Length == 0)
        {
            done(results);
            return;
        }

        int pending = expressions.Length;
        for (int i = 0; i < expressions.Length; i++)
        {
            int slot = i;
            DkmLanguageExpression expression = DkmLanguageExpression.Create(inspectionContext.Language, DkmEvaluationFlags.TreatAsExpression, expressions[i], new RetryMarker());
            inspectionContext.EvaluateExpression(workList, expression, stackFrame, result =>
            {
                results[slot] = result.ErrorCode == 0 ? result.ResultObject as DkmSuccessEvaluationResult : null;
                if (Interlocked.Decrement(ref pending) == 0)
                {
                    done(results);
                }
            });
        }
    }

    private void GetMapChildren(
        DkmSuccessEvaluationResult result,
        MapData map,
        DkmWorkList workList,
        DkmInspectionContext inspectionContext,
        DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
    {
        DkmEvaluationResult[] pseudo = [Pseudo(result, inspectionContext, "[len]", "len", map.Length.ToString())];

        void OnlyPseudo()
        {
            completionRoutine(new DkmGetChildrenAsyncResult(
                pseudo,
                DkmEvaluationResultEnumContext.Create(pseudo.Length, result.StackFrame, inspectionContext, new MapEnum(result, pseudo, [], null))));
        }

        string keyC = ToCType(map.KeyType);
        string valueC = ToCType(map.ValueType);
        // Sizes come from the members of the C++ type of `data` (struct { key, value, hash, ... } *), which needs no type name:
        // proc-local types are spelled `main::main::Triple::$553`, which the C++ evaluator cannot parse.
        string[] expressions = [$"{result.FullName}.data", $"sizeof({result.FullName}.data->key)", $"sizeof({result.FullName}.data->value)"];
        EvalBatch(inspectionContext, workList, result.StackFrame, expressions, values =>
        {
            try
            {
                if (values[0] is null || values[1] is null || values[2] is null)
                {
                    SpikeLog.Write($"map {result.FullName} : {result.Type}: header evaluations failed ({Describe(values[0])} | {Describe(values[1])} | {Describe(values[2])})");
                    OnlyPseudo();
                    return;
                }

                ulong data = ParseUnsigned(values[0]!.Value);
                ulong keySize = ParseUnsigned(values[1]!.Value);
                ulong valueSize = ParseUnsigned(values[2]!.Value);
                ulong capacity = data == 0 ? 0 : 1UL << (int)(data & 63);
                if (capacity == 0 || capacity > MaxMapCapacity)
                {
                    SpikeLog.Write($"map {result.FullName}: capacity {capacity} (data=0x{data:x}) not shown");
                    OnlyPseudo();
                    return;
                }

                ulong keys = data & ~63UL;
                ulong vals = keys + CellArrayBytes(capacity, keySize);
                ulong hashes = vals + CellArrayBytes(capacity, valueSize);
                byte[] hashBytes = new byte[capacity * 8];
                inspectionContext.RuntimeInstance.Process.ReadMemory(hashes, DkmReadMemoryFlags.None, hashBytes);

                var slots = new List<ulong>();
                for (ulong i = 0; i < capacity; i++)
                {
                    ulong hash = BitConverter.ToUInt64(hashBytes, (int)(i * 8));
                    if (hash != 0 && (hash >> 63) == 0)
                    {
                        slots.Add(i);
                    }
                }

                SpikeLog.Write($"map {result.FullName} : {result.Type}: cap={capacity} used={slots.Count} keySize={keySize} valueSize={valueSize} keys=0x{keys:x} vals=0x{vals:x} hashes=0x{hashes:x}");
                var layout = new MapLayout(keys, vals, keySize, valueSize, keyC, valueC);
                completionRoutine(new DkmGetChildrenAsyncResult(
                    pseudo,
                    DkmEvaluationResultEnumContext.Create(pseudo.Length + slots.Count, result.StackFrame, inspectionContext, new MapEnum(result, pseudo, slots, layout))));
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"map children of {result.FullName} failed: {ex}");
                OnlyPseudo();
            }
        });
    }

    private void GetMapItems(
        MapEnum map,
        DkmWorkList workList,
        int startIndex,
        int count,
        DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
    {
        var items = new List<DkmEvaluationResult>();
        int index = startIndex;
        while (items.Count < count && index < map.Pseudo.Count)
        {
            items.Add(map.Pseudo[index]);
            index++;
        }

        int firstSlot = index - map.Pseudo.Count;
        int slotCount = Math.Max(0, Math.Min(count - items.Count, map.Slots.Count - firstSlot));
        if (slotCount == 0 || map.Layout is null)
        {
            completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
            return;
        }

        MapLayout layout = map.Layout;
        DkmInspectionContext inspectionContext = map.Parent.InspectionContext;
        // Elements 2*i (key) and 2*i+1 (value); each has one or more spellings of the cast, tried in one flat batch.
        var expressions = new List<string>();
        var firstVariant = new int[slotCount * 2 + 1];
        for (int i = 0; i < slotCount; i++)
        {
            ulong slot = map.Slots[firstSlot + i];
            firstVariant[i * 2] = expressions.Count;
            expressions.AddRange(CastVariants(layout.KeyC, layout.Keys + ElementOffset(slot, layout.KeySize)));
            firstVariant[i * 2 + 1] = expressions.Count;
            expressions.AddRange(CastVariants(layout.ValueC, layout.Values + ElementOffset(slot, layout.ValueSize)));
        }

        firstVariant[slotCount * 2] = expressions.Count;

        EvalBatch(inspectionContext, workList, map.Parent.StackFrame, expressions.ToArray(), variantResults =>
        {
            var results = new DkmSuccessEvaluationResult?[slotCount * 2];
            for (int element = 0; element < results.Length; element++)
            {
                for (int v = firstVariant[element]; v < firstVariant[element + 1]; v++)
                {
                    if (variantResults[v] is not null)
                    {
                        results[element] = variantResults[v];
                        if (v != firstVariant[element] && Interlocked.Increment(ref m_VariantLogCount) <= LoggedSequenceLimit)
                        {
                            SpikeLog.Write($"cast spelling worked on variant {v - firstVariant[element]}: {expressions[v]}");
                        }

                        break;
                    }
                }
            }

            try
            {
                for (int i = 0; i < slotCount; i++)
                {
                    DkmSuccessEvaluationResult? key = results[i * 2];
                    DkmSuccessEvaluationResult? value = results[i * 2 + 1];
                    if (key is null || value is null)
                    {
                        SpikeLog.Write($"map entry failed: {expressions[firstVariant[i * 2]]} -> {Describe(key)}; {expressions[firstVariant[i * 2 + 1]]} -> {Describe(value)}");
                        items.Add(Pseudo(map.Parent, inspectionContext, $"[slot {map.Slots[firstSlot + i]}]", "?", "<unreadable>"));
                        continue;
                    }

                    DkmEvaluationResult transformed = Transform(value);
                    DkmSuccessEvaluationResult shown = transformed as DkmSuccessEvaluationResult ?? value;
                    items.Add(DkmSuccessEvaluationResult.Create(
                        inspectionContext,
                        map.Parent.StackFrame,
                        $"[{key.Value}]",
                        value.FullName,
                        shown.Flags,
                        shown.Value,
                        shown.EditableValue,
                        shown.Type,
                        shown.Category,
                        shown.Access,
                        shown.StorageType,
                        shown.TypeModifierFlags,
                        shown.Address,
                        shown.CustomUIVisualizers,
                        shown.ExternalModules,
                        new RetypeData(shown)));
                }

                completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"map items failed: {ex}");
                completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
            }
        });
    }

    private static string Describe(DkmEvaluationResult? result)
    {
        return result is DkmSuccessEvaluationResult s
            ? $"'{s.Name}' {s.Type} {s.Value}"
            : result is null ? "(null)" : $"{result.GetType().Name} '{result.FullName}'";
    }

    // Marks an evaluation result as one of ours so GetChildren knows to build len/cap/elements.
    private sealed class SequenceData(long length, long? capacity, bool isDynamic) : DkmDataItem
    {
        public long Length { get; } = length;

        public long? Capacity { get; } = capacity;

        public bool IsDynamic { get; } = isDynamic;
    }

    // Marks a matrix result; `rows` are the row texts as shown (`1, 2, 3`), whatever the storage order.
    private sealed class MatrixData(IReadOnlyList<string> rows, int columns, string elementType, string order) : DkmDataItem
    {
        public IReadOnlyList<string> Rows { get; } = rows;

        public int Columns { get; } = columns;

        public string ElementType { get; } = elementType;

        public string Order { get; } = order;
    }

    // Marks a map result; GetChildren decodes the runtime layout.
    private sealed class MapData(string keyType, string valueType, long length) : DkmDataItem
    {
        public string KeyType { get; } = keyType;

        public string ValueType { get; } = valueType;

        public long Length { get; } = length;
    }

    private sealed class MapLayout(ulong keys, ulong values, ulong keySize, ulong valueSize, string keyC, string valueC)
    {
        public ulong Keys { get; } = keys;

        public ulong Values { get; } = values;

        public ulong KeySize { get; } = keySize;

        public ulong ValueSize { get; } = valueSize;

        public string KeyC { get; } = keyC;

        public string ValueC { get; } = valueC;
    }

    // `[len]` followed by one `[key]` child per used slot, whose key and value are read on demand.
    private sealed class MapEnum(
        DkmSuccessEvaluationResult parent,
        IReadOnlyList<DkmEvaluationResult> pseudo,
        IReadOnlyList<ulong> slots,
        MapLayout? layout) : DkmDataItem
    {
        public DkmSuccessEvaluationResult Parent { get; } = parent;

        public IReadOnlyList<DkmEvaluationResult> Pseudo { get; } = pseudo;

        public IReadOnlyList<ulong> Slots { get; } = slots;

        public MapLayout? Layout { get; } = layout;

        public void GetItems(
            OdinEvaluator evaluator,
            DkmWorkList workList,
            int startIndex,
            int count,
            DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
        {
            evaluator.GetMapItems(this, workList, startIndex, count, completionRoutine);
        }
    }

    // Tags an expression as an attempt issued by EvaluateWithRewrites.
    private sealed class RetryMarker : DkmDataItem
    {
    }

    // A result we re-created with an Odin type name; children, editing and strings come from the original.
    private sealed class RetypeData(DkmSuccessEvaluationResult original) : DkmDataItem
    {
        public DkmSuccessEvaluationResult Original { get; } = original;
    }

    // An enum context that forwards to the C++ evaluator's and transforms what comes back.
    private sealed class WrappedEnum(DkmEvaluationResultEnumContext inner) : DkmDataItem
    {
        public DkmEvaluationResultEnumContext Inner { get; } = inner;
    }

    // `[len]`, `[cap]` pseudo-children followed by the C++ evaluator's element children.
    private sealed class SequenceEnum(
        IReadOnlyList<DkmEvaluationResult> pseudo,
        DkmEvaluationResultEnumContext? elements,
        Func<DkmEvaluationResult[], DkmEvaluationResult[]> transform) : DkmDataItem
    {
        public void GetItems(
            DkmWorkList workList,
            int startIndex,
            int count,
            DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
        {
            var items = new List<DkmEvaluationResult>();
            int index = startIndex;
            int remaining = count;
            while (remaining > 0 && index < pseudo.Count)
            {
                items.Add(pseudo[index]);
                index++;
                remaining--;
            }

            if (remaining <= 0 || elements is null)
            {
                completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
                return;
            }

            elements.GetItems(workList, index - pseudo.Count, remaining, elementItems =>
            {
                if (elementItems.ErrorCode != 0 || elementItems.Items is null)
                {
                    completionRoutine(elementItems);
                    return;
                }

                items.AddRange(transform(elementItems.Items));
                completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
            });
        }
    }
}
