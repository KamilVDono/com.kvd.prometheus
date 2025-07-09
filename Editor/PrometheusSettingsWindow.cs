using System;
using System.IO;
using System.Runtime.CompilerServices;
using KVD.Utils.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace KVD.Prometheus.Editor
{
	public class PrometheusSettingsWindow : EditorWindow
	{
		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/Settings", false, 11)]
		private static void ShowWindow()
		{
			var window = GetWindow<PrometheusSettingsWindow>();
			window.titleContent = new GUIContent("Prometheus settings");
			window.Show();
		}

		void OnGUI()
		{
			var settings = PrometheusSettings.Instance;
			var serializedSettings = new SerializedObject(settings);

			EditorGUILayout.LabelField("Prometheus Settings", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			serializedSettings.Update();

			var prop = serializedSettings.GetIterator();

			var requiresDomainReload = false;
			EditorGUI.BeginChangeCheck();

			for (var expanded = true; prop.NextVisible(expanded); expanded = false)
			{
				if (prop.name == "m_Script")
				{
					continue;
				}

				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(prop, true);
				if (EditorGUI.EndChangeCheck() && prop.ExtractAttribute<DomainBoundAttribute>() != null)
				{
					requiresDomainReload = true;
				}
			}

			var changed = EditorGUI.EndChangeCheck();

			serializedSettings.ApplyModifiedProperties();

			if (changed)
			{
				settings.OnValidate();

				if (requiresDomainReload)
				{
					var filePath = GetThisFilePath();
					File.SetLastWriteTime(filePath, DateTime.Now);
					AssetDatabase.Refresh();
					CompilationPipeline.RequestScriptCompilation();
				}
			}
		}

		static string GetThisFilePath([CallerFilePath] string path = null) {
			return path;
		}
	}
}
