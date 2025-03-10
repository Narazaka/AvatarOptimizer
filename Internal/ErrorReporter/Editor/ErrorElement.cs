using System;
using System.Linq;
using CustomLocalization4EditorExtension;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Anatawa12.AvatarOptimizer.ErrorReporting
{
    internal class ErrorElement : Box
    {
        private readonly ErrorLog log;

        Texture2D GetIcon()
        {
            switch (log.reportLevel)
            {
                case ReportLevel.Info:
                    return EditorGUIUtility.FindTexture("d_console.infoicon");
                case ReportLevel.Warning:
                    return EditorGUIUtility.FindTexture("d_console.warnicon");
                default:
                    return EditorGUIUtility.FindTexture("d_console.erroricon");
            }
        }

        public ErrorElement(ErrorLog log, ObjectRefLookupCache cache)
        {
            this.log = log;

            AddToClassList("ErrorElement");
            var tex = GetIcon();
            if (tex != null)
            {
                var image = new Image();
                image.image = tex;
                Add(image);
            }

            var inner = new Box();
            Add(inner);

            var label = new Label(GetLabelText());
            inner.Add(label);

            foreach (var obj in log.referencedObjects)
            {
                var referenced = obj.Lookup(cache);
                if (referenced != null)
                {
                    inner.Add(new SelectionButton(obj.typeName, referenced));
                }
            }

            if (!string.IsNullOrWhiteSpace(log.stacktrace))
            {
                var foldout = new Foldout();
                foldout.text = CL4EE.Tr("ErrorReporting:error.stack_trace");
                var field = new TextField();
                field.value = log.stacktrace;
                field.isReadOnly = true;
                field.multiline = true;
                foldout.Add(field);
                foldout.value = false;
                inner.Add(foldout);
            }
        }

        private static GameObject FindObject(string path)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == path) return root;
                if (path.StartsWith(root.name + "/"))
                {
                    return root.transform.Find(path.Substring(root.name.Length + 1))?.gameObject;
                }
            }

            return null;
        }

        private string GetLabelText()
        {
            try
            {
                var assembly = log.MessageAssembly;
                var localization = assembly != null ? CL4EE.GetLocalization(assembly) : null;
                var formatKey = localization != null ? localization.Tr(log.messageCode) : log.messageCode;
                return string.Format(formatKey, log.substitutions.ToArray<object>());
            }
            catch (FormatException e)
            {
                Debug.LogError("Error formatting message code: " + log.messageCode);
                Debug.LogException(e);
                return log.messageCode + "\n" + string.Join("\n", log.substitutions);
            }
        }
    }
}
