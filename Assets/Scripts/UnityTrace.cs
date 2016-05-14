using ActionStreetMap.Infrastructure.Diagnostic;
using System;
using System.Text;
using UnityEngine;

namespace MapGen {
    public class UnityTrace : DefaultTrace {
        protected override void WriteRecord(RecordType type, string category, string message, Exception exception) {
            var logMessage = ToLogMessage(type, category, message, exception);
            switch (type) {
                case RecordType.Error:
                    UnityEngine.Debug.LogError(logMessage);
                    break;
                case RecordType.Warn:
                    UnityEngine.Debug.LogWarning(logMessage);
                    break;
                default:
                    UnityEngine.Debug.Log(logMessage);
                    break;
            }
        }

        private string ToLogMessage(RecordType type, string category, string text, Exception exception) {
            switch (type) {
                case RecordType.Error:
                    return String.Format("[{0}] {1}:{2}. Exception: {3}", type, category, text, exception);
                case RecordType.Warn:
                    return String.Format("[{0}] {1}:{2}", type, category, text);
                case RecordType.Info:
                    var lines = text.Trim('\n').Split('\n');
                    var output = new StringBuilder();
                    output.Append(String.Format("[{0}] {1}:", type, category));
                    for (int i = 0; i < lines.Length; i++)
                        output.AppendFormat("{0}{1}", lines[i], i != lines.Length - 1 ? "\n" : "");
                    return output.ToString();
                default:
                    return String.Format("[{0}] {1}: {2}", type, category, text);
            }
        }
    }
}
