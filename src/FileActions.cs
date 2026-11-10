using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Windows.Input;

namespace WSLBox
{
    [DataContract] public sealed class FileFailure
    {
        [DataMember] public string Path;
        [DataMember] public string Error;
    }
    [DataContract] internal sealed class FileRequest
    {
        [DataMember(EmitDefaultValue = false)] public string[] Paths;
        [DataMember(EmitDefaultValue = false)] public string Path;
        [DataMember(EmitDefaultValue = false)] public string Name;
    }
    internal static class FileKeyRouting
    {
        public static bool DeferToFocusedControl(bool textInput, bool buttonInput, bool collapsed, Key key, ModifierKeys modifiers)
        {
            if (textInput || (buttonInput && (key == Key.Enter || key == Key.Space))) return true;
            if (!collapsed) return false;
            return key == Key.Delete || key == Key.F2 || key == Key.Enter
                || ((modifiers & ModifierKeys.Control) != 0 && (key == Key.A || key == Key.C || key == Key.X || key == Key.V));
        }
    }
    internal static class FileActions
    {
        public static IEnumerable<string[]> Batches(string[] paths)
        {
            var batch = new List<string>(); int characters = 0;
            foreach (string path in paths)
            {
                if (batch.Count > 0 && (batch.Count == 32 || characters + path.Length > 6000))
                { yield return batch.ToArray(); batch.Clear(); characters = 0; }
                batch.Add(path); characters += path.Length + 8;
            }
            if (batch.Count > 0) yield return batch.ToArray();
        }
    }
}
