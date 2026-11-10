using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Evaluation;
using OdinVS.Core;

namespace OdinVS.Debugger;

// A language evaluator in front of the C++ one (LanguageId C, which is what the Odin PDB reports). Every call is
// chained to the next implementation, and what comes back is adjusted:
//  C: results whose type is an Odin slice `[]T` or dynamic array `[dynamic]T` are replaced by our own result that shows
//     `len`/`cap` and expands to `[len]`, `[cap]` and the elements. The elements come from the C++ evaluator itself
//     (`<name>.data,<len>`), so element display and nested expansion keep working.
//  D1: C scalar type names are shown with their Odin names (`__int64` -> `int`, `__int64 *` -> `^int`). The PDB cannot
//     tell `int` from `i64`, so this is a best guess (see FINDINGS.md). Retyped results forward everything else to the original.
//  D2: Watch expressions the C evaluator rejects are retried as Odin: `len(x)` -> `x.len`, `a[i]` -> `a.data[i]`; the index is
//     checked against `len` (an out-of-range index is an error), a fixed array is range-checked from its type name, and
//     `m[key]` on a `map[K]V` is looked up in the debuggee's memory (LookupMapEntry).
// Anything we cannot parse or read is returned unchanged. Public because the dispatcher instantiates it by name.
public sealed class OdinEvaluator : IDkmLanguageExpressionEvaluator
{
    private const long MaxSaneLength = 100_000_000;
    private const int MaxIndexRewrites = 4;
    private const int LoggedSequenceLimit = 12;
    private const double SlowTransformMilliseconds = 20;

    private static readonly Regex m_SliceType = new(@"^\[\](?<elem>.+)$", RegexOptions.Compiled);
    private static readonly Regex m_DynamicType = new(@"^\[dynamic\](?<elem>.+)$", RegexOptions.Compiled);
    private static readonly Regex m_FixedPrefix = new(@"^\[(?<n>\d+)\]", RegexOptions.Compiled);
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

    private static readonly Regex m_Signed = new(@"^\s*(?<sign>-?)(?<n>0x[0-9a-fA-F]+|\d+)", RegexOptions.Compiled);

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
    private const int MaxShownSlots = 1000;
    private const int LoggedFailureLimit = 12;

    private static int m_VariantLogCount;
    private static int m_FailureLogCount;
    private static int m_SequenceLogCount;

    public OdinEvaluator()
    {
        DebuggerLog.Write("OdinEvaluator constructed");
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

    // Evaluates `first`. If the C evaluator rejects it and the text has `[`, Odin indexing is tried by inserting `.data`
    // before some of the brackets (Odin indexes the slice, C indexes a pointer). Which brackets belong to slices is not
    // known without types, so every subset is tried (at most 2^MaxIndexRewrites) in ONE flat batch issued from the first
    // failure's callback, and the first success in subset order wins. Each attempt comes with a bounds condition
    // (`i >= 0 && i < a.len`) in the same batch, so an index outside the container is an error instead of memory past its end.
    // Chaining the retries (each started from the previous one's callback) hung at the third level (`things[0].tags[1]`); a
    // thread-pool workaround crashed devenv or hung too, so nothing here nests deeper than two calls and no thread is created.
    // Attempts carry RetryMarker so they are not rewritten again.
    //
    // `name[key]` on a map is looked up in the debuggee's memory (LookupMapEntry). The map's type, header and the key are
    // not known before `first` fails, and a third level is not allowed, so for text of that shape the probes are issued
    // together with `first` (still one flat level) and used only if `first` fails.
    private void EvaluateWithRewrites(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression original,
        DkmLanguageExpression first,
        string firstText,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        OdinWatchExpressions.MapLookupShape? shape = OdinWatchExpressions.SplitLookup(firstText);
        if (shape is null)
        {
            inspectionContext.EvaluateExpression(workList, first, stackFrame, firstResult =>
                ContinueAfterFirst(inspectionContext, workList, original, firstText, stackFrame, firstResult, null, completionRoutine));
            return;
        }

        string[] probeExpressions =
        [
            shape.Base,
            $"{shape.Base}.data",
            $"sizeof({shape.Base}.data->key)",
            $"sizeof({shape.Base}.data->value)",
            $"(__int64)({shape.Index})",
            $"(unsigned __int64)&({shape.Index})",
        ];
        DkmEvaluateExpressionAsyncResult? firstOutcome = null;
        DkmSuccessEvaluationResult?[]? probeValues = null;
        int remaining = 2;
        void OneDone()
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                ContinueAfterFirst(inspectionContext, workList, original, firstText, stackFrame, firstOutcome!.Value, new MapProbe(shape, probeValues!), completionRoutine);
            }
        }

        inspectionContext.EvaluateExpression(workList, first, stackFrame, firstResult =>
        {
            firstOutcome = firstResult;
            OneDone();
        });
        EvalBatch(inspectionContext, workList, stackFrame, probeExpressions, values =>
        {
            probeValues = values;
            OneDone();
        });
    }

    private void ContinueAfterFirst(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression original,
        string firstText,
        DkmStackWalkFrame stackFrame,
        DkmEvaluateExpressionAsyncResult firstResult,
        MapProbe? probe,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        if (!IsFailure(firstResult))
        {
            // A fixed array is indexed natively, which does not check the index: the probe has its length in the type name.
            string? outOfRange = probe is null ? null : FixedArrayProblem(probe);
            CloseProbe(probe);
            if (outOfRange is not null)
            {
                CloseResult(firstResult.ResultObject);
                completionRoutine(Failed(inspectionContext, stackFrame, original.Text, outOfRange));
                return;
            }

            TransformAllAsync([firstResult.ResultObject!], inspectionContext, workList, transformed =>
                completionRoutine(new DkmEvaluateExpressionAsyncResult(transformed[0])));
            return;
        }

        if (probe?.Values[MapProbe.BaseValue] is DkmSuccessEvaluationResult mapResult && mapResult.Type is not null && TryParseMapType(mapResult.Type, out _, out _))
        {
            LookupMapEntry(inspectionContext, workList, original, stackFrame, probe, mapResult, completionRoutine);
            return;
        }

        CloseProbe(probe);
        IReadOnlyList<int> brackets = OdinWatchExpressions.FindBrackets(firstText, MaxIndexRewrites);
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

        OdinWatchExpressions.IndexRewrite[] rewrites = [.. masks.Select(m => OdinWatchExpressions.Rewrite(firstText, brackets, m))];
        DkmEvaluateExpressionAsyncResult?[] results = new DkmEvaluateExpressionAsyncResult?[masks.Count];
        // Per attempt and per inserted index: the index as a number and the container's `len`, for the bounds check and its message.
        DkmEvaluateExpressionAsyncResult?[][] bounds = [.. rewrites.Select(r => new DkmEvaluateExpressionAsyncResult?[r.Checks.Count * 2])];
        int pending = masks.Count + bounds.Sum(b => b.Length);

        void OneAttemptDone()
        {
            if (Interlocked.Decrement(ref pending) != 0)
            {
                return;
            }

            int chosen = -1;
            for (int i = 0; i < results.Length && chosen < 0; i++)
            {
                if (!IsFailure(results[i]!.Value))
                {
                    chosen = i;
                }
            }

            string? problem = chosen >= 0 ? BoundsProblem(rewrites[chosen], bounds[chosen], original.Text) : null;
            for (int i = 0; i < results.Length; i++)
            {
                foreach (DkmEvaluateExpressionAsyncResult? measured in bounds[i])
                {
                    CloseResult(measured?.ResultObject);
                }

                if (i != chosen || problem is not null)
                {
                    CloseResult(results[i]!.Value.ResultObject);
                }
            }

            if (problem is not null)
            {
                DebuggerLog.Write($"expression '{original.Text}' rejected: {problem}");
                completionRoutine(Failed(inspectionContext, stackFrame, original.Text, problem));
                return;
            }

            if (chosen >= 0)
            {
                DebuggerLog.Write($"expression '{original.Text}' evaluated as '{rewrites[chosen].Text}'");
                // Already two calls deep: no further evaluation here (see above), so a cut-off header is not resolved.
                completionRoutine(new DkmEvaluateExpressionAsyncResult(Transform(results[chosen]!.Value.ResultObject!)));
                return;
            }

            completionRoutine(firstResult);
        }

        for (int k = 0; k < masks.Count; k++)
        {
            int slot = k;
            DkmLanguageExpression attempt = DkmLanguageExpression.Create(original.Language, original.CompilationFlags, rewrites[slot].Text, new RetryMarker());
            inspectionContext.EvaluateExpression(workList, attempt, stackFrame, result =>
            {
                results[slot] = result;
                OneAttemptDone();
            });
            for (int c = 0; c < rewrites[slot].Checks.Count; c++)
            {
                OdinWatchExpressions.IndexCheck check = rewrites[slot].Checks[c];
                string[] measures = [$"(__int64)({check.Index})", $"{check.Base}.len"];
                for (int m = 0; m < 2; m++)
                {
                    int measureSlot = c * 2 + m;
                    DkmLanguageExpression measure = DkmLanguageExpression.Create(original.Language, original.CompilationFlags, measures[m], new RetryMarker());
                    inspectionContext.EvaluateExpression(workList, measure, stackFrame, result =>
                    {
                        bounds[slot][measureSlot] = result;
                        OneAttemptDone();
                    });
                }
            }
        }
    }

    // Null when every index is inside its container, or when that cannot be told (no `.len`: not an Odin container, shown as evaluated).
    private static string? BoundsProblem(OdinWatchExpressions.IndexRewrite rewrite, DkmEvaluateExpressionAsyncResult?[] measured, string text)
    {
        for (int c = 0; c < rewrite.Checks.Count; c++)
        {
            DkmEvaluateExpressionAsyncResult? indexResult = measured[c * 2];
            DkmEvaluateExpressionAsyncResult? lengthResult = measured[c * 2 + 1];
            if (indexResult is null || lengthResult is null
                || IsFailure(indexResult.Value) || IsFailure(lengthResult.Value)
                || indexResult.Value.ResultObject is not DkmSuccessEvaluationResult indexValue
                || lengthResult.Value.ResultObject is not DkmSuccessEvaluationResult lengthValue
                || !TryParseSigned(indexValue.Value, out long index)
                || !TryParseSigned(lengthValue.Value, out long length))
            {
                DebuggerLog.Write($"bounds of {rewrite.Checks[c].Index} in {rewrite.Checks[c].Base} (in '{text}') could not be checked, shown without a check");
                continue;
            }

            if (index < 0 || index >= length)
            {
                return $"index {index} is out of bounds of {rewrite.Checks[c].Base} (length {length})";
            }
        }

        return null;
    }

    private static string? FixedArrayProblem(MapProbe probe)
    {
        string? type = (probe.Values[MapProbe.BaseValue] as DkmSuccessEvaluationResult)?.Type;
        if (type is null || probe.Values[MapProbe.IndexNumber] is not DkmSuccessEvaluationResult number || !TryParseSigned(number.Value, out long index))
        {
            return null;
        }

        Match odinStyle = m_FixedPrefix.Match(type);
        Match cStyle = m_FixedType.Match(type);
        string? length = odinStyle.Success ? odinStyle.Groups["n"].Value : cStyle.Success ? cStyle.Groups["n"].Value : null;
        if (length is null || !long.TryParse(length, out long count) || (index >= 0 && index < count))
        {
            return null;
        }

        return $"index {index} is out of bounds of {probe.Shape.Base} (length {count})";
    }

    private static DkmEvaluateExpressionAsyncResult Failed(DkmInspectionContext inspectionContext, DkmStackWalkFrame stackFrame, string text, string message)
    {
        return new DkmEvaluateExpressionAsyncResult(DkmFailedEvaluationResult.Create(inspectionContext, stackFrame, text, text, message, DkmEvaluationResultFlags.None, null, null));
    }

    private static void CloseProbe(MapProbe? probe)
    {
        if (probe is null)
        {
            return;
        }

        foreach (DkmSuccessEvaluationResult? value in probe.Values)
        {
            CloseResult(value);
        }
    }

    // What EvaluateWithRewrites learns about `Base[Index]` next to evaluating it: the evaluations of the map base, its `data`
    // member, the key and value sizes (as GetMapChildren reads them), and the index as a number and as an address
    // (a result of this kind of evaluation carries no Address of its own). Null where one failed.
    private sealed class MapProbe(OdinWatchExpressions.MapLookupShape shape, DkmSuccessEvaluationResult?[] values)
    {
        public OdinWatchExpressions.MapLookupShape Shape { get; } = shape;

        public DkmSuccessEvaluationResult?[] Values { get; } = values;

        public const int BaseValue = 0;
        public const int DataValue = 1;
        public const int KeySizeValue = 2;
        public const int ValueSizeValue = 3;
        public const int IndexNumber = 4;
        public const int IndexAddress = 5;
    }

    // Longest string key or map storage that is searched; beyond it the lookup says so instead of reading it.
    private const long MaxLookupBytes = 64L * 1024 * 1024;
    private const int MaxStringKeyReads = 200_000;

    // `map[key]` typed in Watch: the keys are searched in the debuggee's memory (no hashing, so nothing depends on Odin's hasher):
    // the used slots come from the hash array as when expanding a map, each key is compared with the typed one, and the
    // value of the slot found is evaluated with the same cast the map's children use. A missing key is an error (Odin would
    // give a zero value, which a debugger should not invent). The result's FullName is the text that was typed, not the
    // address-based cast: Visual Studio saves and re-evaluates a Watch entry by FullName, and an address is stale after the next run.
    private void LookupMapEntry(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression original,
        DkmStackWalkFrame stackFrame,
        MapProbe probe,
        DkmSuccessEvaluationResult mapResult,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        bool answered = false;
        void Answer(DkmEvaluateExpressionAsyncResult answer)
        {
            if (!answered)
            {
                answered = true;
                CloseProbe(probe);
                completionRoutine(answer);
            }
        }

        void Fail(string message)
        {
            DebuggerLog.Write($"map lookup '{original.Text}': {message}");
            Answer(Failed(inspectionContext, stackFrame, original.Text, message));
        }

        try
        {
            if (!TryParseMapType(mapResult.Type ?? string.Empty, out string keyType, out string valueType))
            {
                Fail("not a map");
                return;
            }

            DkmSuccessEvaluationResult? dataValue = probe.Values[MapProbe.DataValue];
            DkmSuccessEvaluationResult? keySizeValue = probe.Values[MapProbe.KeySizeValue];
            DkmSuccessEvaluationResult? valueSizeValue = probe.Values[MapProbe.ValueSizeValue];
            if (dataValue is null || keySizeValue is null || valueSizeValue is null)
            {
                Fail($"the header of {probe.Shape.Base} could not be read");
                return;
            }

            ulong keySize = ParseUnsigned(keySizeValue.Value);
            ulong valueSize = ParseUnsigned(valueSizeValue.Value);
            OdinMapLayout.MapArrays arrays = OdinMapLayout.Arrays(ParseUnsigned(dataValue.Value), keySize, valueSize);
            if (arrays.Capacity == 0)
            {
                Fail($"key {probe.Shape.Index} not found: {probe.Shape.Base} is empty");
                return;
            }

            if (arrays.Capacity > MaxMapCapacity || keySize == 0 || arrays.Values - arrays.Keys > (ulong)MaxLookupBytes)
            {
                Fail($"{probe.Shape.Base} is too large to search (capacity {arrays.Capacity}, key size {keySize})");
                return;
            }

            string keyC = ToCType(keyType);
            string valueC = ToCType(valueType);
            DkmProcess process = inspectionContext.RuntimeInstance.Process;
            bool isString = keyC == OdinStringType;
            byte[]? target = SearchKey(process, probe, keyC, keySize, isString, out string? keyProblem);
            if (target is null)
            {
                Fail(keyProblem ?? "the key could not be read");
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            ulong? slot = FindSlot(process, arrays, keySize, isString, target, out int used, out int comparisons, out string? scanProblem);
            DebuggerLog.Write($"map lookup '{original.Text}': {used} used slots, {comparisons} string reads, {(slot is null ? "not found" : $"slot {slot}")}, {timer.ElapsedMilliseconds} ms");
            if (slot is null)
            {
                Fail(scanProblem ?? $"key {probe.Shape.Index} not found in {probe.Shape.Base} ({used} entries)");
                return;
            }

            if (valueSize == 0)
            {
                // The set idiom `map[K]struct{}`: the key being there is the whole answer.
                Answer(new DkmEvaluateExpressionAsyncResult(DkmSuccessEvaluationResult.Create(
                    inspectionContext,
                    stackFrame,
                    original.Text,
                    original.Text,
                    DkmEvaluationResultFlags.ReadOnly,
                    "{}",
                    null,
                    "struct{}",
                    DkmEvaluationResultCategory.Data,
                    DkmEvaluationResultAccessType.None,
                    DkmEvaluationResultStorageType.None,
                    DkmEvaluationResultTypeModifierFlags.None,
                    null,
                    null,
                    null,
                    null)));
                return;
            }

            ulong valueAddress = arrays.Values + OdinMapLayout.ElementOffset(slot.Value, valueSize);
            string rest = probe.Shape.Rest;
            string[] expressions = [.. CastVariants(valueC, valueAddress).Select(v => rest.Length == 0 ? v : $"({v}){rest}")];
            EvalBatch(inspectionContext, workList, stackFrame, expressions, values =>
            {
                try
                {
                    DkmSuccessEvaluationResult? value = null;
                    foreach (DkmSuccessEvaluationResult? candidate in values)
                    {
                        if (value is null)
                        {
                            value = candidate;
                        }
                        else
                        {
                            CloseResult(candidate);
                        }
                    }

                    if (value is null)
                    {
                        Fail($"the value of {original.Text} could not be read ({Describe(null)})");
                        return;
                    }

                    // A string value is read from memory (no further evaluation: this is already two levels deep).
                    DkmEvaluationResult transformed = valueC == OdinStringType && rest.Length == 0
                        && ReadStringHeader(process, valueAddress) is string valueText
                            ? ShowString(value, valueText)
                            : Transform(value);
                    DkmSuccessEvaluationResult shown = transformed as DkmSuccessEvaluationResult ?? value;
                    DebuggerLog.Write($"map lookup '{original.Text}': value type {valueC}, shown as {(ReferenceEquals(shown, value) ? "native" : "decoded")}: {shown.Value}");
                    Answer(new DkmEvaluateExpressionAsyncResult(DkmSuccessEvaluationResult.Create(
                        inspectionContext,
                        stackFrame,
                        original.Text,
                        original.Text,
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
                        new RetypeData(shown))));
                }
                catch (Exception ex)
                {
                    DebuggerLog.Write($"map lookup '{original.Text}' failed building the result: {ex}");
                    Fail($"the value of {original.Text} could not be shown: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            DebuggerLog.Write($"map lookup '{original.Text}' failed: {ex}");
            Fail($"the lookup failed: {ex.Message} (see odinvs-debugger.log)");
        }
    }

    // The bytes a stored key must have to equal the typed one: the text of a string (a literal, or a string variable read from
    // memory), the number for keys of at most eight bytes, else the raw bytes of a variable of the key's type.
    private static byte[]? SearchKey(DkmProcess process, MapProbe probe, string keyC, ulong keySize, bool isString, out string? problem)
    {
        problem = null;
        DkmSuccessEvaluationResult? number = probe.Values[MapProbe.IndexNumber];
        if (isString)
        {
            if (OdinWatchExpressions.TryParseStringLiteral(probe.Shape.Index, out byte[] literal))
            {
                return literal;
            }

            ulong address = AddressOf(probe);
            if (address != 0)
            {
                byte[] header = new byte[16];
                process.ReadMemory(address, DkmReadMemoryFlags.None, header);
                ulong data = BitConverter.ToUInt64(header, 0);
                long length = BitConverter.ToInt64(header, 8);
                if (length is >= 0 and <= MaxLookupBytes && (data != 0 || length == 0))
                {
                    byte[] text = new byte[length];
                    if (length > 0)
                    {
                        process.ReadMemory(data, DkmReadMemoryFlags.None, text);
                    }

                    return text;
                }
            }

            problem = $"the key {probe.Shape.Index} must be a string literal or a string variable";
            return null;
        }

        bool floating = keyC is "float" or "double";
        if (!floating && keySize <= 8 && number is not null && TryParseSigned(number.Value, out long numeric))
        {
            return OdinWatchExpressions.LittleEndian(numeric, (int)keySize);
        }

        ulong variable = AddressOf(probe);
        if (variable != 0 && keySize <= MaxLookupBytes)
        {
            byte[] raw = new byte[keySize];
            process.ReadMemory(variable, DkmReadMemoryFlags.None, raw);
            return raw;
        }

        problem = $"the key {probe.Shape.Index} must be a number, or a variable of the key type {keyC}";
        return null;
    }

    private static bool BytesEqual(byte[] buffer, int offset, byte[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (buffer[offset + i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    // Address of the typed key when it is a variable (the `&(index)` probe), else 0.
    private static ulong AddressOf(MapProbe probe)
    {
        DkmSuccessEvaluationResult? address = probe.Values[MapProbe.IndexAddress];
        return address is not null && TryParseSigned(address.Value, out long value) ? (ulong)value : 0;
    }

    // Slot of the used entry whose key equals `target`, or null. String keys are compared by length first and their text is
    // read only for equal lengths, at most MaxStringKeyReads times.
    private static ulong? FindSlot(DkmProcess process, OdinMapLayout.MapArrays arrays, ulong keySize, bool isString, byte[] target, out int used, out int comparisons, out string? problem)
    {
        problem = null;
        used = 0;
        comparisons = 0;
        byte[] hashBytes = new byte[arrays.Capacity * 8];
        process.ReadMemory(arrays.Hashes, DkmReadMemoryFlags.None, hashBytes);
        byte[] keyBytes = new byte[arrays.Values - arrays.Keys];
        process.ReadMemory(arrays.Keys, DkmReadMemoryFlags.None, keyBytes);
        ulong? found = null;
        for (ulong i = 0; i < arrays.Capacity; i++)
        {
            if (!OdinMapLayout.IsUsedHash(BitConverter.ToUInt64(hashBytes, (int)(i * 8))))
            {
                continue;
            }

            used++;
            if (found is not null)
            {
                continue;
            }

            int offset = (int)OdinMapLayout.ElementOffset(i, keySize);
            if (!isString)
            {
                if ((ulong)target.Length == keySize && BytesEqual(keyBytes, offset, target))
                {
                    found = i;
                }

                continue;
            }

            if (BitConverter.ToInt64(keyBytes, offset + 8) != target.Length)
            {
                continue;
            }

            if (target.Length == 0)
            {
                found = i;
                continue;
            }

            if (comparisons >= MaxStringKeyReads)
            {
                problem = $"the search stopped after {MaxStringKeyReads} string comparisons without finding the key";
                continue;
            }

            comparisons++;
            try
            {
                byte[] stored = new byte[target.Length];
                process.ReadMemory(BitConverter.ToUInt64(keyBytes, offset), DkmReadMemoryFlags.None, stored);
                if (BytesEqual(stored, 0, target))
                {
                    found = i;
                }
            }
            catch (Exception ex)
            {
                // A key whose text cannot be read (a dangling pointer) is skipped; the other keys are still searched.
                if (Interlocked.Increment(ref m_FailureLogCount) <= LoggedFailureLimit)
                {
                    DebuggerLog.Write($"map lookup: the text of the key in slot {i} could not be read: {ex.Message} (further failures are not logged)");
                }
            }
        }

        return found;
    }

    // Results that are not handed back to Visual Studio (failed or unused attempts) are closed so the debugger can release them.
    private static void CloseResult(DkmEvaluationResult? result)
    {
        try
        {
            result?.Close();
        }
        catch (Exception ex)
        {
            DebuggerLog.Write($"closing an evaluation result failed (harmless, the debugger releases it later): {ex.Message}");
        }
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
            wrapped.Inner.GetItems(workList, startIndex, count, items =>
            {
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
        List<string> expressions = [];
        List<(int Index, bool Dynamic, int First)> wanted = [];
        List<(int Index, int Expression)> unions = [];
        List<(int Index, int First, int Rows, int Columns)> matrices = [];
        List<(int Index, int First)> stringHeaders = [];
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is not DkmSuccessEvaluationResult success || success.Type is null)
            {
                continue;
            }

            // An Odin string (a `{data, len}` struct): a global natvis entry used to show it (removed, it also changed C++ types named `string`); the component shows it now.
            // With an address the header is read from memory in Transform; without one `.data` and `.len` are evaluated here.
            if (success.Type == OdinStringType)
            {
                if ((success.Address?.Value ?? 0) == 0)
                {
                    stringHeaders.Add((i, expressions.Count));
                    expressions.Add($"{success.FullName}.data");
                    expressions.Add($"{success.FullName}.len");
                }

                continue;
            }

            // A matrix: every element is evaluated (`data[c][r]` column-major, `data[r][c]` row-major) so the rows can be shown.
            Match matrix = m_Matrix.Match(success.Type);
            if (matrix.Success)
            {
                // Odin caps matrix dimensions far below this; a parse failure (or overflow) just leaves the matrix native.
                if (int.TryParse(matrix.Groups["r"].Value, out int rows) && int.TryParse(matrix.Groups["c"].Value, out int columns)
                    && rows is > 0 and <= MaxMatrixCells && columns is > 0 and <= MaxMatrixCells && rows * columns <= MaxMatrixCells)
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
            Match union = m_Union.Match(success.Value ?? string.Empty);
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
            // The length is always evaluated, never read from the value text: the text is cut off, and once `string` stopped being
            // a natvis type (F4) the text of a map or a slice of structs contains the `len=` of nested strings, which a
            // right-to-left search took for the container's own length (maps showed `len=68169720922112`).
            if (!isHeaderType)
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

        int frameIndex = wanted.Count > 0
            ? wanted[0].Index
            : unions.Count > 0 ? unions[0].Index : matrices.Count > 0 ? matrices[0].Index : stringHeaders[0].Index;
        EvalBatch(inspectionContext, workList, results[frameIndex].StackFrame, expressions.ToArray(), values =>
        {
            // `done` runs exactly once, after the try: a throw from inside it must not be answered with a second call.
            DkmEvaluationResult[] transformed;
            try
            {
                Dictionary<DkmEvaluationResult, string> strings = new();
                foreach ((int index, int first) in stringHeaders)
                {
                    // A garbage `data` or `len` (such as -1) skips that one string, never the rest of the batch.
                    if (values[first] is null || values[first + 1] is null
                        || !TryParseSigned(values[first]!.Value, out long dataAddress)
                        || !TryParseSigned(values[first + 1]!.Value, out long stringLength))
                    {
                        DebuggerLog.Write($"string {results[index].FullName}: .data/.len did not evaluate to plain numbers, shown natively");
                        continue;
                    }

                    if (ReadStringBody(inspectionContext.RuntimeInstance.Process, (ulong)dataAddress, stringLength) is string text)
                    {
                        strings[results[index]] = text;
                    }
                }

                Dictionary<DkmEvaluationResult, string[]> matrixRows = new();
                foreach ((int index, int first, int rows, int columns) in matrices)
                {
                    string[] cells = new string[rows * columns];
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
                        DebuggerLog.Write($"matrix {results[index].FullName}: elements failed to evaluate, shown natively");
                    }
                }

                Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult> variants = new();
                foreach ((int index, int expression) in unions)
                {
                    if (values[expression] is DkmSuccessEvaluationResult variant)
                    {
                        variants[results[index]] = variant;
                    }
                    else
                    {
                        DebuggerLog.Write($"union variant of {results[index].FullName} failed to evaluate");
                    }
                }

                Dictionary<DkmEvaluationResult, long[]> headers = new();
                foreach ((int index, bool dynamic, int first) in wanted)
                {
                    DkmSuccessEvaluationResult? len = values[first];
                    DkmSuccessEvaluationResult? cap = dynamic ? values[first + 1] : null;
                    if (len is null || (dynamic && cap is null))
                    {
                        DebuggerLog.Write($"header expressions for {results[index].FullName} failed");
                        continue;
                    }

                    // A garbage len such as -1 does not parse as unsigned: skip that one result, keep the rest of the batch
                    // (an exception here used to drop the union variants of every result in it).
                    if (!TryParseSigned(len.Value, out long lenValue) || (dynamic && !TryParseSigned(cap!.Value, out _)))
                    {
                        DebuggerLog.Write($"header of {results[index].FullName} is not a plain number ('{len.Value}'), shown natively");
                        continue;
                    }

                    TryParseSigned(dynamic ? cap!.Value : "0", out long capValue);
                    headers[results[index]] = [lenValue, dynamic ? capValue : -1];
                }

                transformed = TransformAll(results, headers, variants, matrixRows, strings);
            }
            catch (Exception ex)
            {
                DebuggerLog.Write($"resolving headers failed: {ex}");
                try
                {
                    transformed = TransformAll(results);
                }
                catch (Exception fallback)
                {
                    DebuggerLog.Write($"fallback transform failed too, returning the native results: {fallback}");
                    transformed = results;
                }
            }

            done(transformed);
        });
    }

    private DkmEvaluationResult[] TransformAll(
        DkmEvaluationResult[] results,
        Dictionary<DkmEvaluationResult, long[]>? headers = null,
        Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>? variants = null,
        Dictionary<DkmEvaluationResult, string[]>? matrices = null,
        Dictionary<DkmEvaluationResult, string>? strings = null)
    {
        long started = Stopwatch.GetTimestamp();
        DkmEvaluationResult[] transformed = new DkmEvaluationResult[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            transformed[i] = Transform(results[i], headers, variants, matrices, strings);
        }

        // Measured in Exp (M5.5): about 130-280 us per call. Only a slow call is logged.
        double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        if (ms > SlowTransformMilliseconds)
        {
            DebuggerLog.Write($"WARNING: transforming {results.Length} evaluation results took {ms:F1} ms; the Locals window may feel slow");
        }

        return transformed;
    }

    private DkmEvaluationResult Transform(
        DkmEvaluationResult result,
        Dictionary<DkmEvaluationResult, long[]>? headers = null,
        Dictionary<DkmEvaluationResult, DkmSuccessEvaluationResult>? variants = null,
        Dictionary<DkmEvaluationResult, string[]>? matrices = null,
        Dictionary<DkmEvaluationResult, string>? strings = null)
    {
        try
        {
            if (result is not DkmSuccessEvaluationResult success || success.Type is null)
            {
                return result;
            }

            if (success.Type == OdinStringType)
            {
                string? text = strings is not null && strings.TryGetValue(success, out string? resolved)
                    ? resolved
                    : ReadStringHeader(success.RuntimeInstance.Process, success.Address?.Value ?? 0);
                return text is null ? result : ShowString(success, text);
            }

            Match matrix = m_Matrix.Match(success.Type);
            if (matrix.Success)
            {
                return matrices is not null && matrices.TryGetValue(success, out string[]? cells)
                    ? TransformMatrix(success, matrix, cells)
                    : result;
            }

            Match union = m_Union.Match(success.Value ?? string.Empty);
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
            DebuggerLog.Write($"transform failed for {result.FullName}: {ex}");
            return result;
        }
    }

    // Odin `string` is `Raw_String {data: [^]byte, len: int}` (base/runtime/core.odin, checked by OdinMapLayoutTests). It has no
    // terminating NUL, so the text is read by length; at most MaxStringBytes are shown, with `...` after them.
    private const string OdinStringType = "string";
    private const int MaxStringBytes = 256;

    private static string? ReadStringHeader(DkmProcess process, ulong address)
    {
        if (address == 0)
        {
            return null;
        }

        try
        {
            byte[] header = new byte[16];
            process.ReadMemory(address, DkmReadMemoryFlags.None, header);
            return ReadStringBody(process, BitConverter.ToUInt64(header, 0), BitConverter.ToInt64(header, 8));
        }
        catch (Exception ex)
        {
            DebuggerLog.Write($"reading a string header at 0x{address:x} failed, shown natively: {ex.Message}");
            return null;
        }
    }

    private static string? ReadStringBody(DkmProcess process, ulong data, long length)
    {
        if (length < 0 || length > MaxSaneLength || (data == 0 && length > 0))
        {
            return null;
        }

        try
        {
            int take = (int)Math.Min(length, MaxStringBytes);
            byte[] bytes = new byte[take];
            if (take > 0)
            {
                process.ReadMemory(data, DkmReadMemoryFlags.None, bytes);
            }

            StringBuilder text = new("\"");
            foreach (char c in System.Text.Encoding.UTF8.GetString(bytes))
            {
                switch (c)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\t':
                        text.Append("\\t");
                        break;
                    default:
                        text.Append(c < ' ' ? $"\\x{(int)c:x2}" : c.ToString());
                        break;
                }
            }

            text.Append('"');
            if (length > take)
            {
                text.Append("...");
            }

            return text.ToString();
        }
        catch (Exception ex)
        {
            DebuggerLog.Write($"reading string bytes at 0x{data:x} (len {length}) failed, shown natively: {ex.Message}");
            return null;
        }
    }

    // The string text as the value; the `data` and `len` fields stay available as children of the original result.
    private static DkmEvaluationResult ShowString(DkmSuccessEvaluationResult success, string text)
    {
        return DkmSuccessEvaluationResult.Create(
            success.InspectionContext,
            success.StackFrame,
            success.Name,
            success.FullName,
            DkmEvaluationResultFlags.Expandable | DkmEvaluationResultFlags.ReadOnly,
            text,
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
        string[] rowTexts = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            string[] row = new string[columns];
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
        List<DkmEvaluationResult> rows = [];
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
            DebuggerLog.Write($"sequence {success.FullName} : {success.Type} -> {value} (header via {how}; further sequences are not logged)");
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
                DebuggerLog.Write($"memory read for {result.FullName} failed, using text: {ex.Message}");
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
        List<DkmEvaluationResult> pseudo = new()
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
                DebuggerLog.Write($"element view '{expression}' failed: {Describe(evaluated.ResultObject)}");
                OnlyPseudo();
                return;
            }

            // The array view may itself have been retyped by us (pointer to ^T); expand the underlying C++ result.
            DkmEvaluationResult expandable = array.GetDataItem<RetypeData>() is RetypeData retyped ? retyped.Original : array;
            expandable.GetChildren(workList, initialRequestSize, inspectionContext, children =>
            {
                if (children.ErrorCode != 0 || children.EnumContext is null)
                {
                    DebuggerLog.Write($"element children of '{expression}' failed: error={children.ErrorCode}");
                    OnlyPseudo();
                    return;
                }

                List<DkmEvaluationResult> initial = [.. pseudo];
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

    private static DkmEvaluationResult Pseudo(DkmSuccessEvaluationResult parent, DkmInspectionContext inspectionContext, string name, string member, string value, string type = "int")
    {
        return DkmSuccessEvaluationResult.Create(
            inspectionContext,
            parent.StackFrame,
            name,
            $"{parent.FullName}.{member}",
            DkmEvaluationResultFlags.ReadOnly,
            value,
            null,
            type,
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

    // `*(T*)0xaddr` for the C++ evaluator. Odin names like `main::Thing` do not always cast: `*(T*)` works for `string`, `__int64`
    // and names without a qualifier, and `*(::T*)` for qualified ones (the spellings that worked in the M5.5 experiments; the
    // others tried there never did). The second spelling is only issued for a qualified name.
    private static IEnumerable<string> CastVariants(string cType, ulong address)
    {
        string at = $"0x{address:x}";
        yield return $"*({cType}*){at}";
        if (cType.IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            yield return $"*(::{cType}*){at}";
        }
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
            if (!ulong.TryParse(number.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }
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

        string number = match.Value.Trim();
        bool parsed = number.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(number.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong hex)
            : ulong.TryParse(number, out hex);
        return parsed ? hex : throw new FormatException($"number out of range in '{text}'");
    }

    private static DkmEvaluationResult? TransformMap(DkmSuccessEvaluationResult success, string key, string value, Dictionary<DkmEvaluationResult, long[]>? headers)
    {
        long length;
        if (headers is not null && headers.TryGetValue(success, out long[]? resolved))
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
        DkmSuccessEvaluationResult?[] results = new DkmSuccessEvaluationResult?[expressions.Length];
        if (expressions.Length == 0)
        {
            done(results);
            return;
        }

        int pending = expressions.Length;
        int[] finished = new int[expressions.Length];
        void Finish(int slot)
        {
            // A slot counts once, even if its callback ran and a throw from the issuing call is then caught as well.
            if (Interlocked.Exchange(ref finished[slot], 1) == 0 && Interlocked.Decrement(ref pending) == 0)
            {
                done(results);
            }
        }

        for (int i = 0; i < expressions.Length; i++)
        {
            int slot = i;
            // A throw while issuing must still count the slot as finished (as failed), or `done` would never run.
            try
            {
                DkmLanguageExpression expression = DkmLanguageExpression.Create(inspectionContext.Language, DkmEvaluationFlags.TreatAsExpression, expressions[i], new RetryMarker());
                inspectionContext.EvaluateExpression(workList, expression, stackFrame, result =>
                {
                    results[slot] = result.ErrorCode == 0 ? result.ResultObject as DkmSuccessEvaluationResult : null;
                    if (results[slot] is null)
                    {
                        CloseResult(result.ResultObject);
                    }

                    Finish(slot);
                });
            }
            catch (Exception ex)
            {
                DebuggerLog.Write($"could not issue '{expressions[i]}': {ex.Message}");
                Finish(slot);
            }
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

        // The completion runs once: if it throws inside the try below, the catch must not answer a second time.
        bool answered = false;
        void Answer(DkmGetChildrenAsyncResult answer)
        {
            if (!answered)
            {
                answered = true;
                completionRoutine(answer);
            }
        }

        void OnlyPseudo()
        {
            Answer(new DkmGetChildrenAsyncResult(
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
                    DebuggerLog.Write($"map {result.FullName} : {result.Type}: header evaluations failed ({Describe(values[0])} | {Describe(values[1])} | {Describe(values[2])})");
                    OnlyPseudo();
                    return;
                }

                ulong data = ParseUnsigned(values[0]!.Value);
                ulong keySize = ParseUnsigned(values[1]!.Value);
                ulong valueSize = ParseUnsigned(values[2]!.Value);
                OdinMapLayout.MapArrays arrays = OdinMapLayout.Arrays(data, keySize, valueSize);
                ulong capacity = arrays.Capacity;
                if (capacity == 0 || capacity > MaxMapCapacity)
                {
                    DebuggerLog.Write($"map {result.FullName}: capacity {capacity} (data=0x{data:x}) not shown");
                    OnlyPseudo();
                    return;
                }

                ulong keys = arrays.Keys;
                ulong vals = arrays.Values;
                ulong hashes = arrays.Hashes;
                byte[] hashBytes = new byte[capacity * 8];
                inspectionContext.RuntimeInstance.Process.ReadMemory(hashes, DkmReadMemoryFlags.None, hashBytes);

                List<ulong> slots = [];
                for (ulong i = 0; i < capacity; i++)
                {
                    ulong hash = BitConverter.ToUInt64(hashBytes, (int)(i * 8));
                    if (OdinMapLayout.IsUsedHash(hash))
                    {
                        slots.Add(i);
                    }
                }

                DebuggerLog.Write($"map {result.FullName} : {result.Type}: cap={capacity} used={slots.Count} keySize={keySize} valueSize={valueSize} keys=0x{keys:x} vals=0x{vals:x} hashes=0x{hashes:x}");
                MapLayout layout = new(keys, vals, keySize, valueSize, keyC, valueC);
                DkmEvaluationResult[] shownPseudo = pseudo;
                if (slots.Count > MaxShownSlots)
                {
                    // Every shown entry costs one or two evaluator calls, so a huge map is cut and says so.
                    shownPseudo = [.. pseudo, Pseudo(result, inspectionContext, "[shown]", "shown", $"first {MaxShownSlots} of {slots.Count}")];
                    slots.RemoveRange(MaxShownSlots, slots.Count - MaxShownSlots);
                }

                Answer(new DkmGetChildrenAsyncResult(
                    shownPseudo,
                    DkmEvaluationResultEnumContext.Create(shownPseudo.Length + slots.Count, result.StackFrame, inspectionContext, new MapEnum(result, shownPseudo, slots, layout))));
            }
            catch (Exception ex)
            {
                DebuggerLog.Write($"map children of {result.FullName} failed: {ex}");
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
        List<DkmEvaluationResult> items = [];
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
        List<string> expressions = [];
        int[] firstVariant = new int[slotCount * 2 + 1];
        for (int i = 0; i < slotCount; i++)
        {
            ulong slot = map.Slots[firstSlot + i];
            firstVariant[i * 2] = expressions.Count;
            expressions.AddRange(CastVariants(layout.KeyC, layout.Keys + OdinMapLayout.ElementOffset(slot, layout.KeySize)));
            firstVariant[i * 2 + 1] = expressions.Count;
            if (layout.ValueSize > 0)
            {
                // A zero-sized value (the set idiom `map[K]struct{}`) has nothing to read and no type to cast to.
                expressions.AddRange(CastVariants(layout.ValueC, layout.Values + OdinMapLayout.ElementOffset(slot, layout.ValueSize)));
            }
        }

        firstVariant[slotCount * 2] = expressions.Count;

        EvalBatch(inspectionContext, workList, map.Parent.StackFrame, expressions.ToArray(), variantResults =>
        {
            DkmSuccessEvaluationResult?[] results = new DkmSuccessEvaluationResult?[slotCount * 2];
            for (int element = 0; element < results.Length; element++)
            {
                for (int v = firstVariant[element]; v < firstVariant[element + 1]; v++)
                {
                    if (variantResults[v] is not null && results[element] is not null)
                    {
                        // A later spelling that also worked (all are issued at once) is not needed.
                        CloseResult(variantResults[v]);
                    }
                    else if (variantResults[v] is not null)
                    {
                        results[element] = variantResults[v];
                        if (v != firstVariant[element] && Interlocked.Increment(ref m_VariantLogCount) <= LoggedSequenceLimit)
                        {
                            DebuggerLog.Write($"cast spelling worked on variant {v - firstVariant[element]}: {expressions[v]}");
                        }
                    }
                }
            }

            bool answered = false;
            void Answer()
            {
                if (!answered)
                {
                    answered = true;
                    completionRoutine(new DkmEvaluationEnumAsyncResult(items.ToArray()));
                }
            }

            try
            {
                for (int i = 0; i < slotCount; i++)
                {
                    DkmSuccessEvaluationResult? key = results[i * 2];
                    DkmSuccessEvaluationResult? value = results[i * 2 + 1];
                    if (key is not null && layout.ValueSize == 0)
                    {
                        string setKey = layout.KeyC == OdinStringType
                            ? ReadStringHeader(inspectionContext.RuntimeInstance.Process, layout.Keys + OdinMapLayout.ElementOffset(map.Slots[firstSlot + i], layout.KeySize)) ?? key.Value ?? string.Empty
                            : key.Value ?? string.Empty;
                        items.Add(Pseudo(map.Parent, inspectionContext, $"[{setKey}]", "?", "{}", "struct{}"));
                        continue;
                    }

                    if (key is null || value is null)
                    {
                        // A zero-sized value has no expression of its own (empty range), so name it only when there is one.
                        string valueExpression = firstVariant[i * 2 + 1] < firstVariant[i * 2 + 2] ? expressions[firstVariant[i * 2 + 1]] : "(none)";
                        if (Interlocked.Increment(ref m_FailureLogCount) <= LoggedFailureLimit)
                        {
                            DebuggerLog.Write($"map entry failed: {expressions[firstVariant[i * 2]]} -> {Describe(key)}; {valueExpression} -> {Describe(value)} (further entry failures are not logged)");
                        }

                        items.Add(Pseudo(map.Parent, inspectionContext, $"[slot {map.Slots[firstSlot + i]}]", "?", "<unreadable>"));
                        continue;
                    }

                    // String keys and values are Odin strings: their header is at a known address, so they are read from memory
                    // (no further evaluation, which would be a third level deep) instead of showing the `{data, len}` struct.
                    ulong slotIndex = map.Slots[firstSlot + i];
                    DkmProcess process = inspectionContext.RuntimeInstance.Process;
                    string? keyText = layout.KeyC == OdinStringType
                        ? ReadStringHeader(process, layout.Keys + OdinMapLayout.ElementOffset(slotIndex, layout.KeySize)) ?? key.Value
                        : key.Value;
                    DkmEvaluationResult transformed = layout.ValueC == OdinStringType
                        && ReadStringHeader(process, layout.Values + OdinMapLayout.ElementOffset(slotIndex, layout.ValueSize)) is string valueText
                            ? ShowString(value, valueText)
                            : Transform(value);
                    DkmSuccessEvaluationResult shown = transformed as DkmSuccessEvaluationResult ?? value;
                    items.Add(DkmSuccessEvaluationResult.Create(
                        inspectionContext,
                        map.Parent.StackFrame,
                        $"[{keyText}]",
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

                Answer();
            }
            catch (Exception ex)
            {
                DebuggerLog.Write($"map items failed: {ex}");
                Answer();
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
            List<DkmEvaluationResult> items = [];
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
