using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KVD.Prometheus.Editor
{
	public static class IsInPrometheusHeader
	{
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
				invalidAssets += AssetDatabase.Contains(targets[i]) ? 0 : 1;
			}
			if (invalidAssets == targets.Length)
			{
				return;
			}

			var mainInContentFiles = 0;
			var exactInContentFiles = 0;

			for (var i = 0; i < targets.Length; i++)
			{
				var target = targets[i];
				var mainAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(targets[i]));
				exactInContentFiles += PrometheusAssets.HasExact(target) ? 1 : 0;
				mainInContentFiles += s_mainAssets.Contains(mainAsset) ? 1 : 0;
			}

			if (targets.Length == 1)
			{
				var assetReference = PrometheusReferenceUtils.GetFromAssetNoAdd(targets[0]);
				GUILayout.BeginHorizontal();

				var isPresentExact = exactInContentFiles == 1;
				var shouldBePresent = isPresentExact;

				if (exactInContentFiles == 1)
				{
					shouldBePresent = GUILayout.Toggle(true, $"Is in Prometheus - Exact - {assetReference:N}");
				}
				else if (mainInContentFiles == 1)
				{
					shouldBePresent = GUILayout.Toggle(false, $"In Prometheus - Main asset - {assetReference:N}");
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
				if (exactInContentFiles == targets.Length)
				{
					GUILayout.Label("In Prometheus - Exact");
				}
				else if (mainInContentFiles == targets.Length)
				{
					GUILayout.Label("In Prometheus - Main assets");
				}
				else if (mainInContentFiles > 0 || exactInContentFiles > 0)
				{
					GUILayout.Label($"In Prometheus - {mainInContentFiles}/{targets.Length} main assets, {exactInContentFiles}/{targets.Length} exact");
				}
				else
				{
					GUILayout.Label("None in Prometheus");
				}
			}
		}
	}
}
