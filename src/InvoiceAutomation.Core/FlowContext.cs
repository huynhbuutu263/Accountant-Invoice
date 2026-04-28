using System.Collections;

namespace InvoiceAutomation.Core;

/// <summary>Scoped variables for {{placeholder}} resolution (loop scopes supported).</summary>
public sealed class FlowContext
{
    private readonly Stack<Dictionary<string, string>> _scopes = new();

    public FlowContext(IEnumerable<KeyValuePair<string, string>> rootPairs)
    {
        var root = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in rootPairs)
            root[kv.Key] = kv.Value;
        _scopes.Push(root);
    }

    public void PushScope() =>
        _scopes.Push(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public void PopScope()
    {
        if (_scopes.Count > 1)
            _scopes.Pop();
    }

    public void Set(string key, string value) =>
        _scopes.Peek()[key] = value;

    public bool TryGet(string key, out string value)
    {
        foreach (var dict in _scopes)
        {
            if (dict.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
        }

        value = "";
        return false;
    }

    public string GetOrEmpty(string key) =>
        TryGet(key, out var v) ? v : "";
}
