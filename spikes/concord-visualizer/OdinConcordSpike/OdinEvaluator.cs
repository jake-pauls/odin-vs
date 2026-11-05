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
        inspectionContext.GetFrameArguments(workList, frame, result =>
        {
            completionRoutine(result.ErrorCode != 0 || result.Arguments is null
                ? result
                : new DkmGetFrameArgumentsAsyncResult(TransformAll(result.Arguments)));
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
        if (expression.GetDataItem<RetryMarker>() is not null)
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
                completionRoutine(new DkmEvaluateExpressionAsyncResult(Transform(firstResult.ResultObject!)));
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

        if (result.GetDataItem<MapData>() is MapData map)
        {
            GetMapChildren((DkmSuccessEvaluationResult)result, map, workList, inspectionContext, completionRoutine);
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
            completionRoutine(new DkmGetChildrenAsyncResult(TransformAll(children.InitialChildren), wrapped));
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
                completionRoutine(items.ErrorCode != 0 || items.Items is null
                    ? items
                    : new DkmEvaluationEnumAsyncResult(TransformAll(items.Items)));
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
        if (result.GetDataItem<SequenceData>() is not null || result.GetDataItem<MapData>() is not null)
        {
            errorText = "Odin slices, dynamic arrays and maps cannot be assigned from the debugger window.";
            return;
        }

        DkmEvaluationResult target = result.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : result;
        string? chainedError;
        target.SetValueAsString(value, timeout, out chainedError);
        errorText = chainedError!;
    }

    public string GetUnderlyingString(DkmEvaluationResult result)
    {
        if (result.GetDataItem<SequenceData>() is not null || result.GetDataItem<MapData>() is not null)
        {
            return string.Empty;
        }

        DkmEvaluationResult target = result.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : result;
        return target.GetUnderlyingString()!;
    }

    private DkmEvaluationResult[] TransformAll(DkmEvaluationResult[] results)
    {
        long started = Stopwatch.GetTimestamp();
        var transformed = new DkmEvaluationResult[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            transformed[i] = Transform(results[i]);
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

    private DkmEvaluationResult Transform(DkmEvaluationResult result)
    {
        try
        {
            if (result is not DkmSuccessEvaluationResult success || success.Type is null)
            {
                return result;
            }

            if (TryParseMapType(success.Type, out string mapKey, out string mapValue))
            {
                return TransformMap(success, mapKey, mapValue) ?? result;
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
                return TransformSequence(success, dynamic) ?? result;
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

    private static DkmEvaluationResult? TransformSequence(DkmSuccessEvaluationResult success, bool dynamic)
    {
        if (!TryReadHeader(success, dynamic, out long length, out long? capacity, out string how))
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
                DkmEvaluationResultEnumContext.Create(only.Length, result.StackFrame, inspectionContext, new SequenceEnum(pseudo, null, TransformAll))));
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
                        new SequenceEnum(pseudo, children.EnumContext, TransformAll))));
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

    private static ulong CellSize(ulong size)
    {
        return size > 0 && size < 64 ? 64 : Math.Max(64, (size + 63) / 64 * 64);
    }

    private static ulong CellArrayBytes(ulong capacity, ulong size)
    {
        ulong perCell = size > 0 && size < 64 ? 64 / size : 1;
        return (capacity + perCell - 1) / perCell * CellSize(size);
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

    private static DkmEvaluationResult? TransformMap(DkmSuccessEvaluationResult success, string key, string value)
    {
        Match match = m_Len.Match(success.Value);
        if (!match.Success)
        {
            return null;
        }

        long length = ParseNumber(match.Groups["len"].Value);
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
        string[] expressions = [$"{result.FullName}.data", $"sizeof({keyC})", $"sizeof({valueC})"];
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
        var expressions = new string[slotCount * 2];
        for (int i = 0; i < slotCount; i++)
        {
            ulong slot = map.Slots[firstSlot + i];
            expressions[i * 2] = $"*({layout.KeyC}*)0x{layout.Keys + ElementOffset(slot, layout.KeySize):x}";
            expressions[i * 2 + 1] = $"*({layout.ValueC}*)0x{layout.Values + ElementOffset(slot, layout.ValueSize):x}";
        }

        EvalBatch(inspectionContext, workList, map.Parent.StackFrame, expressions, results =>
        {
            try
            {
                for (int i = 0; i < slotCount; i++)
                {
                    DkmSuccessEvaluationResult? key = results[i * 2];
                    DkmSuccessEvaluationResult? value = results[i * 2 + 1];
                    if (key is null || value is null)
                    {
                        SpikeLog.Write($"map entry failed: {expressions[i * 2]} -> {Describe(key)}; {expressions[i * 2 + 1]} -> {Describe(value)}");
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
