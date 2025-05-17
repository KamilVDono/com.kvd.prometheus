using System.Collections.Generic;
using System.Linq;
using KVD.Utils.DataStructures;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KVD.Prometheus.Editor
{
	[CustomPropertyDrawer(typeof(PrometheusReference))]
	public class PrometheusReferenceDrawer : PropertyDrawer
	{
		static readonly Dictionary<PrometheusIdentifier, Object> AssetCache = new Dictionary<PrometheusIdentifier, Object>
		{
			{ default, null },
		};
		static readonly HashSet<PrometheusIdentifier> LoadingAssets = new HashSet<PrometheusIdentifier>();

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUIUtility.singleLineHeight;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			var assetGuidProp = property.FindPropertyRelative("assetGuid");
			var valueProp = assetGuidProp.FindPropertyRelative("_value");
			var guidV0Prop = valueProp.GetFixedBufferElementAtIndex(0);
			var guidV1Prop = valueProp.GetFixedBufferElementAtIndex(1);

			var localIdentifierProp = property.FindPropertyRelative("localIdentifier");

			var serializableGuid = new SerializableGuid(guidV0Prop.ulongValue, guidV1Prop.ulongValue);
			var assetIdentifier = new PrometheusIdentifier(serializableGuid, localIdentifierProp.longValue);

			var isLoading = LoadingAssets.Contains(assetIdentifier);
			var asset = default(Object);

			if (!isLoading && !AssetCache.TryGetValue(assetIdentifier, out asset))
			{
				var path = AssetDatabase.GUIDToAssetPath(serializableGuid.ToString("N"));
				var loadingAssetIdentifier = assetIdentifier;
				LoadingAssets.Add(loadingAssetIdentifier);
				AssetDatabase.LoadObjectAsync(path, assetIdentifier.localIdentifier).completed += operation =>
				{
					AssetCache[loadingAssetIdentifier] = ((AssetDatabaseLoadOperation)operation).LoadedObject;
					LoadingAssets.Remove(loadingAssetIdentifier);
				};
			}

			if (isLoading)
			{
				EditorGUI.LabelField(position, label, new GUIContent("Loading..."));
				EditorGUI.EndProperty();
				return;
			}

			var assetType = typeof(Object);
			var assetTypeAttribute = fieldInfo.GetCustomAttributes(typeof(PrometheusReferenceTypeAttribute), false).FirstOrDefault();
			if (assetTypeAttribute is PrometheusReferenceTypeAttribute assetTypeAttr)
			{
				assetType = assetTypeAttr.Type;
			}

			EditorGUI.BeginChangeCheck();
			asset = EditorGUI.ObjectField(position, label, asset, assetType, false);
			if (EditorGUI.EndChangeCheck())
			{
				var newGuid = default(SerializableGuid);
				var newLocalIdentifier = 0L;
				if (asset != null)
				{
					if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var newAssetGuid, out var newLocalId))
					{
						newGuid = new(newAssetGuid);
						newLocalIdentifier = newLocalId;

						assetIdentifier = new(newGuid, newLocalIdentifier);
						AssetCache[assetIdentifier] = asset;

						PrometheusAssets.AddIfNotPreset(asset);
					}
				}

				guidV0Prop.ulongValue = SerializableGuid.EditorAccess.Value0(newGuid);
				guidV1Prop.ulongValue = SerializableGuid.EditorAccess.Value1(newGuid);
				localIdentifierProp.longValue = newLocalIdentifier;
			}

			EditorGUI.EndProperty();
		}
	}
}
