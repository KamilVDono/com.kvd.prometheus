using System;
using KVD.Utils.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KVD.Prometheus.Editor
{
	public class AssetDataInfoWindow : EditorWindow
	{
		Object _asset;
		string _guid;
		long _localIdentifier;

		bool _isLocked;

		void OnEnable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
			Selection.selectionChanged += OnSelectionChanged;
		}

		void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
		}

		void OnGUI()
		{
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.BeginHorizontal();
			var iconName = _isLocked ? "LockIcon-On" : "LockIcon";
			var style = new GUIStyle(GUIStyle.none)
			{
				alignment = TextAnchor.MiddleCenter,
			};
			if (GUILayout.Button(EditorGUIUtility.IconContent(iconName), style, GUILayout.Width(20), GUILayout.Height(20)))
			{
				_isLocked = !_isLocked;
			}
			using (new EditorGUI.DisabledScope(_isLocked))
			{
				_asset = EditorGUILayout.ObjectField("Asset", _asset, typeof(Object), false);
			}
			EditorGUILayout.EndHorizontal();
			if (EditorGUI.EndChangeCheck())
			{
				OnNewAsset();
			}

			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.LabelField("GUID", GUILayout.Width(38));
			_guid = EditorGUILayout.TextField(_guid, GUILayout.Width(252));
			EditorGUILayout.LabelField("Local Identifier", GUILayout.Width(86));
			_localIdentifier = EditorGUILayout.LongField(_localIdentifier);
			if (EditorGUI.EndChangeCheck())
			{
				RefreshAssetFromAssetIdentifier();
			}
			EditorGUILayout.EndHorizontal();
		}

		void OnNewAsset()
		{
			if (_asset)
			{
				AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_asset, out _guid, out _localIdentifier);
			}
			else
			{
				_guid = string.Empty;
				_localIdentifier = 0;
			}
		}

		void RefreshAssetFromAssetIdentifier()
		{
			var path = AssetDatabase.GUIDToAssetPath(_guid);
			var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
			_asset = null;
			foreach (var asset in allAssets)
			{
				if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out var localIdentifier) && localIdentifier == _localIdentifier)
				{
					_asset = asset;
					break;
				}
			}
			if (_asset == null)
			{
				_asset = AssetDatabase.LoadMainAssetAtPath(path);
			}
		}

		void OnSelectionChanged()
		{
			if (!_isLocked && Selection.activeObject != null)
			{
				_asset = Selection.activeObject;
				OnNewAsset();
				Repaint();
			}
		}

		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/Asset Data Info Window", false, 100)]
		static void ShowWindow()
		{
			var window = GetWindow<AssetDataInfoWindow>();
			window.titleContent = new("AssetDataInfo");
			window.Show();
		}
	}
}
