using KVD.Utils.Editor;
using UnityEditor;
using UnityEngine;

namespace KVD.Prometheus.Editor
{
	public class AssetDataInfoWindow : EditorWindow
	{
		Object _asset;
		string _guid;
		long _localIdentifier;

		void OnGUI()
		{
			EditorGUI.BeginChangeCheck();
			_asset = EditorGUILayout.ObjectField("Asset", _asset, typeof(Object), false);
			if (EditorGUI.EndChangeCheck())
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

			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			_guid = EditorGUILayout.TextField("GUID", _guid);
			_localIdentifier = EditorGUILayout.LongField("Local Identifier", _localIdentifier);
			if (EditorGUI.EndChangeCheck())
			{
				RefreshAssetFromAssetIdentifier();
			}
			EditorGUILayout.EndHorizontal();
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

		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/Asset Data Info Window", false, 100)]
		static void ShowWindow()
		{
			var window = GetWindow<AssetDataInfoWindow>();
			window.titleContent = new("AssetDataInfo");
			window.Show();
		}
	}
}
