using System;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace OdinConcordSpike;

// B2: a pass-through language evaluator in front of the C++ one. Every call is chained to the next implementation
// (the dispatcher object is the first parameter) and logged, which tests two things at once: whether a component
// can sit in front of the native evaluator and hand calls on to it (A2 caveat 1), and what names, types and values
// the debugger gives us for Odin locals. It changes nothing the user sees. Filtered to LanguageId C, which is what
// the Odin PDB reports (the Call Stack Language column shows C). Public because the dispatcher instantiates it by name.
public sealed class LoggingEvaluator : IDkmLanguageExpressionEvaluator
{
    public LoggingEvaluator()
    {
        SpikeLog.Write("LoggingEvaluator constructed");
    }

    public void GetFrameLocals(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine)
    {
        SpikeLog.Write($"GetFrameLocals: language={inspectionContext.Language?.Name}");
        inspectionContext.GetFrameLocals(workList, stackFrame, result =>
        {
            try
            {
                SpikeLog.Write($"GetFrameLocals result: error={result.ErrorCode} count={result.EnumContext?.Count}");
                if (result.ErrorCode == 0 && result.EnumContext is not null)
                {
                    result.EnumContext.GetItems(workList, 0, result.EnumContext.Count, items =>
                    {
                        LogItems("local", items);
                    });
                }
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"GetFrameLocals logging failed: {ex}");
            }

            completionRoutine(result);
        });
    }

    public void GetFrameArguments(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmStackWalkFrame frame,
        DkmCompletionRoutine<DkmGetFrameArgumentsAsyncResult> completionRoutine)
    {
        inspectionContext.GetFrameArguments(workList, frame, completionRoutine);
    }

    public void EvaluateExpression(
        DkmInspectionContext inspectionContext,
        DkmWorkList workList,
        DkmLanguageExpression expression,
        DkmStackWalkFrame stackFrame,
        DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine)
    {
        SpikeLog.Write($"EvaluateExpression: '{expression.Text}'");
        inspectionContext.EvaluateExpression(workList, expression, stackFrame, result =>
        {
            try
            {
                SpikeLog.Write($"EvaluateExpression result: error={result.ErrorCode} {Describe(result.ResultObject)}");
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"EvaluateExpression logging failed: {ex}");
            }

            completionRoutine(result);
        });
    }

    public void GetChildren(
        DkmEvaluationResult result,
        DkmWorkList workList,
        int initialRequestSize,
        DkmInspectionContext inspectionContext,
        DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine)
    {
        SpikeLog.Write($"GetChildren of {Describe(result)}");
        result.GetChildren(workList, initialRequestSize, inspectionContext, completionRoutine);
    }

    public void GetItems(
        DkmEvaluationResultEnumContext enumContext,
        DkmWorkList workList,
        int startIndex,
        int count,
        DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine)
    {
        enumContext.GetItems(workList, startIndex, count, completionRoutine);
    }

    public void SetValueAsString(DkmEvaluationResult result, string value, int timeout, out string errorText)
    {
        string? chainedError;
        result.SetValueAsString(value, timeout, out chainedError);
        errorText = chainedError!;
    }

    public string GetUnderlyingString(DkmEvaluationResult result)
    {
        return result.GetUnderlyingString()!;
    }

    private static void LogItems(string kind, DkmEvaluationEnumAsyncResult items)
    {
        if (items.ErrorCode != 0)
        {
            SpikeLog.Write($"{kind} items error={items.ErrorCode}");
            return;
        }

        foreach (DkmEvaluationResult item in items.Items)
        {
            SpikeLog.Write($"{kind}: {Describe(item)}");
        }
    }

    private static string Describe(DkmEvaluationResult? result)
    {
        if (result is DkmSuccessEvaluationResult success)
        {
            return $"name='{success.Name}' type='{success.Type}' value='{success.Value}' fullName='{success.FullName}' flags={success.Flags} category={success.Category} storage={success.StorageType}";
        }

        return result is null ? "(null)" : $"{result.GetType().Name} name='{result.Name}' fullName='{result.FullName}'";
    }
}
