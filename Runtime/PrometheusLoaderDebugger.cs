using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using KVD.Utils.GUIs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace KVD.Prometheus
{
	public class PrometheusLoaderDebugger
	{
		Vector2 _globalScroll;

		bool _expandedAssets;
		ImguiTable<PrometheusIdentifier> _assetTable;
		Dictionary<PrometheusIdentifier, GUIContent> _assetTableIdentifierCache;
		Dictionary<PrometheusIdentifier, string> _assetTableFileCache;
		List<PrometheusIdentifier> _assetIdentifiers;
		Vector2 _mappingScroll;

		Vector2 _fileManagementScroll;
		bool _ongoingsExpanded;
		bool _loadedExpanded;
		UnsafeBitmask _expandedLoaded;
		ImguiTable<uint> _loadedTable;
		UnsafeArray<uint> _loadedIndicesCache;

		public void Init()
		{
			_expandedLoaded = new UnsafeBitmask(64, Allocator.Domain);

			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			_assetIdentifiers = new(editorAccess.PrometheusMapping.Asset2LocalIdentifier.Count);
			foreach (var assetIdentifier in editorAccess.PrometheusMapping.Asset2LocalIdentifier.Keys)
			{
				_assetIdentifiers.Add(assetIdentifier);
			}

			_assetTableIdentifierCache = new(_assetIdentifiers.Count);
			_assetTableFileCache = new(_assetIdentifiers.Count);
			_assetTable = new ImguiTable<PrometheusIdentifier>((a, s) =>
				{
					if (s.IsEmpty)
					{
						return true;
					}
					// TODO: Optimize search HasSearchInterest, string.Contains is slow and can be optimized in this case
					return s.HasSearchInterest(a.assetGuid.ToString("N")) || s.HasSearchInterest(ContentFileGuidOfAsset(a));
				},
				ImguiTableUtils.TextColumn<PrometheusIdentifier>("Id", a => new($"{a.assetGuid:N} [{a.localIdentifier}]"), _assetTableIdentifierCache, 356),
				ImguiTableUtils.TextColumn<PrometheusIdentifier>("Content file", ContentFileGuidOfAsset, 296),
				ImguiTableUtils.TextColumn<PrometheusIdentifier>("State", StateOfAsset, 92),
				ImguiTableUtils.ButtonColumn<PrometheusIdentifier>("(Un)load", AssetLoadButtonText, StartAssetToggle, UnLoadEnableState)
#if UNITY_EDITOR
				,
				ImguiTable<PrometheusIdentifier>.ColumnDefinition.Create("Asset", 128, DrawAsset, a => a.assetGuid),
				ImguiTable<PrometheusIdentifier>.ColumnDefinition.Create("Type", 128, DrawType, a => a.assetGuid),
				ImguiTableUtils.ButtonColumn<PrometheusIdentifier>("Editor Select", "Select", SelectAsset)
#endif
				)
				{
					ShowFooter = false,
				};

			_loadedTable = new ImguiTable<uint>(static (a, s) =>
				{
					if (s.IsEmpty)
					{
						return true;
					}
					var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
					return s.HasSearchInterest(editorAccess.ContentFileLoads[a].contentFileGuid.ToString("N"));
				},
				ImguiTableUtils.TextColumn<uint>("ID", i => i.ToString(), 48),
				ImguiTableUtils.TextColumn<uint>("Content file", i => editorAccess.ContentFileLoads[i].contentFileGuid.ToString("N"), 296),
				ImguiTableUtils.TextColumn<uint>("State", i => editorAccess.ContentFileLoads[i].state.ToString(), 92),
				ImguiTable<uint>.ColumnDefinition.Create("Ref count", 92, DrawRefCount, i => editorAccess.ContentFileLoads[i].referenceCount, ImguiTableUtils.FloatDrawer),
				ImguiTable<uint>.ColumnDefinition.Create("Inspect", 64, DrawFileInspect, i => _expandedLoaded[i] ? 1 : 0, ImguiTableUtils.FloatDrawer, FileInspectSort));

			_loadedIndicesCache = new(0, Allocator.Domain);
		}

		public void Shutdown()
		{
			_expandedLoaded.Dispose();
			_assetIdentifiers.Clear();

			_assetTable.Dispose();
			_loadedTable.Dispose();

			_assetTableIdentifierCache.Clear();
			_assetTableFileCache.Clear();

			_loadedIndicesCache.Dispose();
		}

		public void OnGUI()
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);

			_globalScroll = GUILayout.BeginScrollView(_globalScroll);

			DrawMapping(editorAccess);
			GUILayout.Label("", GUI.skin.horizontalSlider);
			DrawFileManagement(editorAccess);
			GUILayout.Label("", GUI.skin.horizontalSlider);
			DrawCallbacks(editorAccess);

			GUILayout.EndScrollView();
		}

		#region Mapping
		void DrawMapping(PrometheusLoader.EditorAccess editorAccess)
		{
			GUILayout.BeginVertical("box");

			GUILayout.Label("Mapping:", UniversalGUILayout.BoldLabel);
			GUILayout.Label($"Registered content files: {editorAccess.PrometheusMapping.ContentFile2Dependencies.Count}");

			_expandedAssets = UniversalGUILayout.Foldout(_expandedAssets, $"Registered assets: {_assetIdentifiers.Count}");
			if (_expandedAssets)
			{
				_mappingScroll = GUILayout.BeginScrollView(_mappingScroll, GUILayout.Height(300));
				if (_assetTable.Draw(_assetIdentifiers, 300, _mappingScroll.y))
				{
					_assetIdentifiers.Sort(_assetTable.Sorter);
				}
				GUILayout.EndScrollView();
			}

			GUILayout.EndVertical();
		}

		string ContentFileGuidOfAsset(PrometheusIdentifier prometheusIdentifier)
		{
			if (!_assetTableFileCache.TryGetValue(prometheusIdentifier, out var cached))
			{
				var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
				if (!editorAccess.PrometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
				{
					cached = "Not in ContentMap";
				}
				else
				{
					cached = contentFileGuid.ToString("N");
				}
				_assetTableFileCache.Add(prometheusIdentifier, cached);
			}

			return cached;
		}

		static string StateOfAsset(PrometheusIdentifier prometheusIdentifier)
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			if (!editorAccess.PrometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				return "Not in ContentMap";
			}
			if (!editorAccess.ContentFile2Index.TryGetValue(contentFileGuid, out var contentFileIndex))
			{
				return "Free";
			}
			ref var load = ref editorAccess.ContentFileLoads[contentFileIndex];
			return load.state.ToStringFast();
		}

		static string AssetLoadButtonText(PrometheusIdentifier prometheusIdentifier)
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			if (!editorAccess.PrometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				return "Unknown";
			}
			return editorAccess.ContentFile2Index.ContainsKey(contentFileGuid) ? "Unload" : "Load";
		}

		static bool UnLoadEnableState(PrometheusIdentifier prometheusIdentifier)
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			if (!editorAccess.PrometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				return false;
			}
			if (!editorAccess.ContentFile2Index.TryGetValue(contentFileGuid, out var contentFileIndex))
			{
				return true;
			}
			ref var load = ref editorAccess.ContentFileLoads[contentFileIndex];
			return load.state is PrometheusLoader.State.Loaded or PrometheusLoader.State.ErrorArchive or PrometheusLoader.State.ErrorContentFiles;
		}

		static void StartAssetToggle(PrometheusIdentifier prometheusIdentifier)
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			var contentFileGuid = editorAccess.PrometheusMapping.Asset2ContentFile[prometheusIdentifier];
			if (editorAccess.ContentFile2Index.ContainsKey(contentFileGuid))
			{
				PrometheusLoader.Instance.StartAssetUnloading(prometheusIdentifier);
			}
			else
			{
				PrometheusLoader.Instance.StartAssetLoading(prometheusIdentifier);
			}
		}

#if UNITY_EDITOR
		static void DrawAsset(in Rect rect, PrometheusIdentifier prometheusIdentifier)
		{
			var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
			var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
			foreach (var asset in allAssets)
			{
				UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out var localId);
				if (localId == prometheusIdentifier.localIdentifier)
				{
					UnityEditor.EditorGUI.ObjectField(rect, asset, asset.GetType(), false);
					return;
				}
			}
		}

		static void DrawType(in Rect rect, PrometheusIdentifier prometheusIdentifier)
		{
			var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
			var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
			foreach (var asset in allAssets)
			{
				UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out var localId);
				if (localId == prometheusIdentifier.localIdentifier)
				{
					UnityEditor.EditorGUI.LabelField(rect, asset.GetType().Name);
					return;
				}
			}
		}

		static void SelectAsset(PrometheusIdentifier prometheusIdentifier)
		{
			var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
			var loading = UnityEditor.AssetDatabase.LoadObjectAsync(path, prometheusIdentifier.localIdentifier);
			loading.completed += _ => { UnityEditor.Selection.activeObject = loading.LoadedObject; };
		}
#endif
		#endregion Mapping

		#region File Management
		void DrawFileManagement(PrometheusLoader.EditorAccess editorAccess)
		{
			GUILayout.BeginVertical("box");

			GUILayout.Label("File management:", UniversalGUILayout.BoldLabel);
			editorAccess.FileManagedPaused = GUILayout.Toggle(editorAccess.FileManagedPaused, "File Managed Paused");
			DrawOngoings(editorAccess);
			DrawLoaded(editorAccess);
			DrawSelectedSomething(editorAccess);

			GUILayout.EndVertical();
		}

		void DrawOngoings(PrometheusLoader.EditorAccess editorAccess)
		{
			_ongoingsExpanded = UniversalGUILayout.Foldout(_ongoingsExpanded, "Ongoings:");

			if (_ongoingsExpanded)
			{
				UniversalGUILayout.BeginIndent();
				var waitingForMounting = 0;
				var waitingForDependencies = 0;
				var waitingForUnloading = 0;
				var waitingForUnmounting = 0;

				foreach (var load in editorAccess.ContentFileLoads)
				{
					if (load.state == PrometheusLoader.State.WaitingForMounting)
					{
						++waitingForMounting;
					}
					else if (load.state == PrometheusLoader.State.WaitingForDependencies)
					{
						++waitingForDependencies;
					}
					else if (load.state == PrometheusLoader.State.WaitingToStartUnloading)
					{
						++waitingForUnloading;
					}
					else if (load.state == PrometheusLoader.State.WaitingForUnmount)
					{
						++waitingForUnmounting;
					}
				}

				GUILayout.BeginHorizontal();
				GUILayout.BeginVertical();
				GUILayout.Label($"Mounting: {editorAccess.OngoingMountingCount}");
				GUILayout.Label($"Content Loading: {editorAccess.OngoingContentLoadingCount}");
				GUILayout.Label($"Unmounting: {editorAccess.OngoingUnmountingCount}");
				GUILayout.Label($"Content Unloading: {editorAccess.OngoingContentUnloadingCount}");
				GUILayout.EndVertical();
				GUILayout.BeginVertical();
				GUILayout.Label($"For mounting: {waitingForMounting}");
				GUILayout.Label($"For dependencies: {waitingForDependencies}");
				GUILayout.Label($"For unloading: {waitingForUnloading}");
				GUILayout.Label($"For unmounting: {waitingForUnmounting}");
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();

				UniversalGUILayout.EndIndent();
			}
		}

		unsafe void DrawLoaded(PrometheusLoader.EditorAccess editorAccess)
		{
			_loadedExpanded = UniversalGUILayout.Foldout(_loadedExpanded, $"Loaded {editorAccess.OccupiedContentFileIndices.CountOnes()}:");

			if (_loadedExpanded)
			{
				_expandedLoaded.EnsureCapacity(editorAccess.OccupiedContentFileIndices.ElementsLength);

				UniversalGUILayout.BeginIndent();
				_fileManagementScroll = GUILayout.BeginScrollView(_fileManagementScroll, GUILayout.Height(300));

				editorAccess.OccupiedContentFileIndices.ToIndicesOfOneArray(Allocator.Temp, out var occupiedIndices);
				// TODO: Not sequence equal but unordered set equal
				if (occupiedIndices.SequenceEqual(_loadedIndicesCache) == false)
				{
					_loadedIndicesCache.Resize(occupiedIndices.Length);
					UnsafeUtility.MemCpy(_loadedIndicesCache.Ptr, occupiedIndices.Ptr, occupiedIndices.Length*sizeof(uint));
					NativeCollectionsExts.Sort(_loadedIndicesCache, new LoadedTableSort<uint>(_loadedTable.Sorter));
				}
				occupiedIndices.Dispose();

				if (_loadedTable.Draw(new ImguiTableUtils.UnsafeArrayWrapper<uint>(_loadedIndicesCache), 300, _fileManagementScroll.y))
				{
					NativeCollectionsExts.Sort(_loadedIndicesCache, new LoadedTableSort<uint>(_loadedTable.Sorter));
				}

				GUILayout.EndScrollView();
				UniversalGUILayout.EndIndent();
			}
		}

		void DrawSelectedSomething(PrometheusLoader.EditorAccess editorAccess)
		{
			foreach (var expandedIndex in _expandedLoaded.EnumerateOnes())
			{
				var load = editorAccess.ContentFileLoads[expandedIndex];
				GUILayout.BeginHorizontal();
				GUILayout.Label($"Content file {load.contentFileGuid.ToString("N")}:");
				if (GUILayout.Button("X", GUILayout.Width(24)))
				{
					_expandedLoaded.Down(expandedIndex);
				}
				GUILayout.EndHorizontal();
				UniversalGUILayout.BeginIndent();
				if (load.state == PrometheusLoader.State.Loaded)
				{
					var objects = load.contentFile.GetObjects();
					GUI.enabled = false;
					foreach (var contentObject in objects)
					{
#if UNITY_EDITOR
						UnityEditor.EditorGUILayout.ObjectField(contentObject, contentObject.GetType(), false);
#else
						GUILayout.Label($"Object: {contentObject}");
#endif
					}
					GUI.enabled = true;
				}
				else
				{
					GUILayout.Label($"Is not successfully loaded");
				}
				UniversalGUILayout.EndIndent();
			}
		}

		void DrawFileInspect(in Rect rect, uint index)
		{
			_expandedLoaded[index] = GUI.Toggle(rect, _expandedLoaded[index], "");
		}

		bool FileInspectSort(uint index)
		{
			return _expandedLoaded[index];
		}

		void DrawRefCount(in Rect rect, uint index)
		{
			var editorAccess = new PrometheusLoader.EditorAccess(PrometheusLoader.Instance);
			var load = editorAccess.ContentFileLoads[index];
			GUI.Label(rect, load.referenceCount.ToString());
		}
		#endregion File Management

		#region Callbacks
		void DrawCallbacks(PrometheusLoader.EditorAccess editorAccess)
		{
			GUILayout.BeginVertical("box");

			GUILayout.Label("Callbacks:", UniversalGUILayout.BoldLabel);
			GUILayout.Label($"Registered callbacks: {editorAccess.LoadingTasksMask.CountOnes()}");
			GUILayout.Label($"Waiting callbacks: {editorAccess.Callbacks.Count(c => c != null)}");

			GUILayout.EndVertical();
		}
		#endregion Callbacks

		public readonly struct UintIListView : IList<uint>
		{
			readonly UnsafeArray<uint> _array;

			public int Count => _array.LengthInt;
			public bool IsReadOnly => true;

			public uint this[int index]
			{
				get => _array[(uint)index];
				set => _array[(uint)index] = value;
			}

			public UintIListView(UnsafeArray<uint> array) : this()
			{
				_array = array;
			}

			public IEnumerator<uint> GetEnumerator()
			{
				throw new NotImplementedException();
			}
			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
			public void Add(uint item)
			{
				throw new NotImplementedException();
			}
			public void Clear()
			{
				throw new NotImplementedException();
			}
			public bool Contains(uint item)
			{
				throw new NotImplementedException();
			}
			public void CopyTo(uint[] array, int arrayIndex)
			{
				throw new NotImplementedException();
			}
			public bool Remove(uint item)
			{
				throw new NotImplementedException();
			}
			public int IndexOf(uint item)
			{
				throw new NotImplementedException();
			}
			public void Insert(int index, uint item)
			{
				throw new NotImplementedException();
			}
			public void RemoveAt(int index)
			{
				throw new NotImplementedException();
			}
		}

		struct LoadedTableSort<T> : IComparer<T> where T : unmanaged
		{
			Comparison<T> _comparison;

			public LoadedTableSort(Comparison<T> comparison)
			{
				_comparison = comparison;
			}

			public int Compare(T x, T y)
			{
				return _comparison(x, y);
			}
		}
	}
}
