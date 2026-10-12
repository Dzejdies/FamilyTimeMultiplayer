using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public sealed class ScenarioReport
{
    private readonly SortedDictionary<string, string> _facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _evidence = new List<string>();

    public ScenarioReport(string scenarioId, string runId)
    {
        ScenarioId = RequireText(scenarioId, nameof(scenarioId));
        RunId = RequireText(runId, nameof(runId));
    }

    public string ScenarioId { get; }
    public string RunId { get; }
    public ScenarioVerdict? Verdict { get; private set; }
    public string? Reason { get; private set; }
    public bool IsComplete => Verdict.HasValue;
    public IReadOnlyDictionary<string, string> Facts => _facts;
    public IReadOnlyList<string> Evidence => _evidence;

    public void AddFact(string key, string value)
    {
        EnsureOpen();
        _facts[RequireText(key, nameof(key))] = value ?? string.Empty;
    }

    public void AddEvidence(string reference)
    {
        EnsureOpen();
        _evidence.Add(RequireText(reference, nameof(reference)));
    }

    public void RemoveFactsByPrefix(string prefix)
    {
        EnsureOpen();
        foreach (string key in _facts.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _facts.Remove(key);
    }

    public void Finish(ScenarioVerdict verdict, string reason)
    {
        EnsureOpen();
        Verdict = verdict;
        Reason = RequireText(reason, nameof(reason));
    }

    public string ToJson()
    {
        var json = new StringBuilder();
        json.Append("{\"scenarioId\":").Append(Quote(ScenarioId));
        json.Append(",\"runId\":").Append(Quote(RunId));
        json.Append(",\"verdict\":").Append(Verdict.HasValue ? Quote(VerdictName(Verdict.Value)) : "null");
        json.Append(",\"reason\":").Append(Reason == null ? "null" : Quote(Reason));
        json.Append(",\"facts\":{");
        json.Append(string.Join(",", _facts.Select(fact => Quote(fact.Key) + ":" + Quote(fact.Value))));
        json.Append("},\"evidence\":[");
        json.Append(string.Join(",", _evidence.Select(Quote)));
        return json.Append("]}").ToString();
    }

    private void EnsureOpen()
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Scenario report is already complete.");
        }
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value;

    private static string VerdictName(ScenarioVerdict verdict) => verdict switch
    {
        ScenarioVerdict.Pass => "PASS",
        ScenarioVerdict.Fail => "FAIL",
        ScenarioVerdict.BlockedPrecondition => "BLOCKED_PRECONDITION",
        ScenarioVerdict.BlockedEnvironment => "BLOCKED_ENVIRONMENT",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict))
    };

    private static string Quote(string value)
    {
        var escaped = new StringBuilder(value.Length + 2).Append('"');
        foreach (char character in value)
        {
            escaped.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => "\\u" + ((int)character).ToString("x4"),
                _ => character.ToString()
            });
        }
        return escaped.Append('"').ToString();
    }
}
