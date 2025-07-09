using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KVD.Prometheus.Editor
{
	public static class IsInPrometheusHeader
	{
		static HashSet<Type> s_excludedTypes = new HashSet<Type>
		{
			typeof(PrometheusAssets),
			typeof(SceneAsset),
		};
		static HashSet<Object> s_mainAssets = new HashSet<Object>();

		[InitializeOnLoadMethod]
		static void InitHeader()
		{
			UnityEditor.Editor.finishedDefaultHeaderGUI -= OnPostHeaderGUI;
			UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;

			PrometheusAssets.onAssetsChanged -= CalculateAssetsCache;
			PrometheusAssets.onAssetsChanged += CalculateAssetsCache;

			CalculateAssetsCache();
		}

		static void CalculateAssetsCache()
		{
			var assets = PrometheusAssets.Instance.assets;
			s_mainAssets.Clear();
			foreach (var asset in assets)
			{
				var mainAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset));
				s_mainAssets.Add(mainAsset);
			}
		}

		static void OnPostHeaderGUI(UnityEditor.Editor editor)
		{
			var targets = editor.targets;

			var invalidAssets = 0;
			for (var i = 0; i < targets.Length; i++)
			{
				invalidAssets += AssetDatabase.Contains(targets[i]) && !s_excludedTypes.Contains(targets[i].GetType()) ? 0 : 1;
			}
			if (invalidAssets == targets.Length)
			{
				return;
			}

			var indirectCount = 0;
			var directCount = 0;

			for (var i = 0; i < targets.Length; i++)
			{
				var target = targets[i];
				var mainAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(targets[i]));
				directCount += PrometheusAssets.HasExact(target) ? 1 : 0;
				indirectCount += s_mainAssets.Contains(mainAsset) ? 1 : 0;
			}

			if (targets.Length == 1)
			{
				var assetReference = PrometheusReferenceUtils.GetFromAssetNoAdd(targets[0]);
				GUILayout.BeginHorizontal();

				var isPresentExact = directCount == 1;
				var shouldBePresent = isPresentExact;

				if (directCount == 1)
				{
					shouldBePresent = GUILayout.Toggle(true, $"Is in Prometheus - Direct - {assetReference:N}");
				}
				else if (indirectCount == 1)
				{
					using var mixedScope = new EditorGUI.MixedValueScope(true);
					shouldBePresent = GUILayout.Toggle(false, $"In Prometheus - Indirect - {assetReference:N}");
				}
				else
				{
					shouldBePresent = GUILayout.Toggle(false, $"Not in Prometheus - {assetReference:N}");
				}

				if (shouldBePresent != isPresentExact)
				{
					if (shouldBePresent)
					{
						PrometheusAssets.AddIfNotPreset(targets[0]);
					}
					else
					{
						PrometheusAssets.Remove(targets[0]);
					}
				}

				GUILayout.EndHorizontal();
			}
			else
			{
				if (directCount == targets.Length)
				{
					GUILayout.Label("In Prometheus - Exact");
				}
				else if (indirectCount == targets.Length)
				{
					GUILayout.Label("In Prometheus - Main assets");
				}
				else if (indirectCount > 0 || directCount > 0)
				{
					GUILayout.Label($"In Prometheus - {directCount}/{targets.Length} direct, {indirectCount}/{targets.Length} indirect");
				}
				else
				{
					GUILayout.Label("None in Prometheus");
				}
			}
		}
	}
}
