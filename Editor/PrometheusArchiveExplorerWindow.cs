using System;
using System.Collections.Generic;
using System.IO;
using KVD.Utils.DataStructures;
using KVD.Utils.Editor;
using Unity.Collections;
using Unity.Content;
using Unity.IO.Archive;
using Unity.Loading;
using UnityEditor;
using UnityEngine;

namespace KVD.Prometheus.Editor
{
	public class PrometheusArchiveExplorerWindow : EditorWindow
	{
		PrometheusMapping _prometheusMapping;
		Vector2 _globalScroll;

		GUIStyle _deleteButtonStyle;
		Texture2D _deleteIcon;

		Texture2D _fileIcon;
		GUIContent _fileIconContent;
		string _filePath;

		ContentNamespace _contentNamespace;

		List<ExploreArchiveHandle> _localHandles = new List<ExploreArchiveHandle>();
		HashSet<string> _toUnload = new HashSet<string>();
		HashSet<string> _expandedArchives = new HashSet<string>();
		HashSet<string> _expandedDependencies = new HashSet<string>();
		HashSet<string> _expandedDependants = new HashSet<string>();

		void OnEnable()
		{
			_deleteIcon = EditorGUIUtility.IconContent("d_TreeEditor.Trash").image as Texture2D;
			if (_deleteIcon == null)
			{
				_deleteIcon = EditorGUIUtility.IconContent("d_winbtn_close").image as Texture2D;
			}

			_fileIcon = EditorGUIUtility.FindTexture("d_Project");
			_fileIconContent = new(_fileIcon, "Browse for a file");

			_prometheusMapping = PrometheusMapping.Fresh();
			if (File.Exists(PrometheusPersistence.MappingsFilePath))
			{
				_prometheusMapping.Deserialize(PrometheusPersistence.MappingsFilePath);
			}

			_contentNamespace = ContentNamespace.GetOrCreateNamespace("ContentExplore");
		}

		void OnDisable()
		{
			foreach (var handle in _localHandles)
			{
				_toUnload.Add(handle.fileName);
			}

			ProcessUnloads();

			_localHandles.Clear();
			_toUnload.Clear();
			_expandedArchives.Clear();
			_expandedDependencies.Clear();
			_expandedDependants.Clear();

			_contentNamespace.Delete();

			_deleteButtonStyle = null;
			_deleteIcon = null;
			_fileIcon = null;
			_fileIconContent = null;
		}

		public void OnGUI()
		{
			if (_deleteButtonStyle == null)
			{
				_deleteButtonStyle = new(EditorStyles.foldoutHeaderIcon);
				var normal = _deleteButtonStyle.normal;
				normal.background = _deleteIcon;
				_deleteButtonStyle.normal = normal;
			}

			_globalScroll = EditorGUILayout.BeginScrollView(_globalScroll);

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.TextField(_filePath);
			if (GUILayout.Button(_fileIconContent, GUILayout.Width(25)))
			{
				var selectedPath = EditorUtility.OpenFilePanel("Select File", PrometheusPersistence.ArchivesDirectoryPath, "");
				// TODO: Also allow files from build
				if (!selectedPath.Contains("ContentFiles") || !selectedPath.Contains("Archives"))
				{
					selectedPath = PrometheusPersistence.ArchivesDirectoryPath;
				}
				_filePath = selectedPath;
			}
			GUI.enabled = !string.IsNullOrEmpty(_filePath);
			if (GUILayout.Button("Load", GUILayout.Width(60)))
			{
				if (!File.Exists(_filePath))
				{
					Debug.LogError($"File {_filePath} does not exist");
					return;

				}
				var fileName = Path.GetFileName(_filePath);
				LoadArchive(_filePath, fileName);
				_expandedArchives.Add(fileName);
			}
			GUI.enabled = true;
			EditorGUILayout.EndHorizontal();

			// Inspection
			EditorGUILayout.LabelField("Selected:", EditorStyles.boldLabel);
			for (var i = _localHandles.Count-1; i >= 0; i--)
			{
				var handle = _localHandles[i];
				if (_toUnload.Contains(handle.fileName))
				{
					continue;
				}
				var archive = handle.archive;
				var expanded = _expandedArchives.Contains(handle.fileName);
				var localIndex = i;
				var newExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, handle.fileName, null, UnloadButton(localIndex), _deleteButtonStyle);
				if (expanded != newExpanded)
				{
					if (newExpanded)
					{
						_expandedArchives.Add(handle.fileName);
					}
					else
					{
						_expandedArchives.Remove(handle.fileName);
					}
				}
				EditorGUILayout.EndFoldoutHeaderGroup();

				if (!expanded)
				{
					continue;
				}
				var guid = new SerializableGuid(handle.fileName);

				++EditorGUI.indentLevel;

				// Archive
				var fileInfo = archive.GetFileInfo();
				foreach (var info in fileInfo)
				{
					var text = $"{info.Filename} ({info.FileSize} bytes)";
					EditorGUILayout.LabelField(text);
				}

				EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

				// Dependencies
				var hasDependencies = _prometheusMapping.ContentFile2Dependencies.TryGetValue(guid, out var dependencies);
				var expandedDependencies = hasDependencies && _expandedDependencies.Contains(handle.fileName);
				if (hasDependencies)
				{
					var newExpandedDependencies = EditorGUILayout.Foldout(expandedDependencies, $"Dependencies {dependencies.Length}:");
					if (newExpandedDependencies != expandedDependencies)
					{
						if (newExpandedDependencies)
						{
							_expandedDependencies.Add(handle.fileName);
						}
						else
						{
							_expandedDependencies.Remove(handle.fileName);
						}
					}
				}
				else
				{
					EditorGUILayout.LabelField("No dependencies");
				}

				if (expandedDependencies)
				{
					foreach (var dependency in dependencies)
					{
						var dependencyGuidString = dependency.ToString("N");
						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.LabelField(dependencyGuidString);
						if (GUILayout.Button("Expand", GUILayout.Width(60)))
						{
							_expandedArchives.Add(dependencyGuidString);
						}
						EditorGUILayout.EndHorizontal();
					}
				}

				EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

				// Dependants
				var hasDependants = _prometheusMapping.ContentFile2Dependants.TryGetValue(guid, out var dependants);
				var expandedDependants = hasDependants && _expandedDependants.Contains(handle.fileName);
				if (hasDependants)
				{
					var newExpandedDependants = EditorGUILayout.Foldout(expandedDependants, $"Dependants {dependants.Length}:");
					if (newExpandedDependants != expandedDependants)
					{
						if (newExpandedDependants)
						{
							_expandedDependants.Add(handle.fileName);
						}
						else
						{
							_expandedDependants.Remove(handle.fileName);
						}
					}
				}
				else
				{
					EditorGUILayout.LabelField("No dependants");
				}
				if (expandedDependants)
				{
					foreach (var dependant in dependants)
					{
						var dependantStringGuid = dependant.ToString("N");
						EditorGUILayout.BeginHorizontal();
						EditorGUILayout.LabelField(dependantStringGuid);
						var isLoaded = _localHandles.Exists(h => h.fileName == dependantStringGuid);
						if (isLoaded)
						{
							if (GUILayout.Button("Expand", GUILayout.Width(60)))
							{
								_expandedArchives.Add(dependantStringGuid);
							}
						}
						else
						{
							if (GUILayout.Button("Load", GUILayout.Width(60)))
							{
								LoadArchive(GetArchivePath(dependantStringGuid), dependantStringGuid);
								_expandedArchives.Add(dependantStringGuid);
							}
						}
						EditorGUILayout.EndHorizontal();
					}
				}

				EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

				// ContentFile
				var contentFiles = handle.contentFile.GetObjects();
				foreach (var contentFile in contentFiles)
				{
					EditorGUILayout.ObjectField(contentFile, contentFile.GetType(), false);
				}

				--EditorGUI.indentLevel;
			}

			EditorGUILayout.EndScrollView();

			ProcessUnloads();

			Action<Rect> UnloadButton(int index)
			{
				return _ => _toUnload.Add(_localHandles[index].fileName);
			}
		}

		ExploreArchiveHandle LoadArchive(string filePath, string fileName)
		{
			var existingIndex = _localHandles.FindIndex(handle => handle.fileName == fileName);
			if (existingIndex != -1)
			{
				_toUnload.Remove(fileName);
				return _localHandles[existingIndex];
			}

			var archive = ArchiveFileInterface.MountAsync(_contentNamespace, filePath, "exp");
			archive.JobHandle.Complete();

			var guid = new SerializableGuid(fileName);
			ContentFile contentFile = default;
			if (_prometheusMapping.ContentFile2Dependencies.TryGetValue(guid, out var dependencies))
			{
				var deps = new NativeArray<ContentFile>(dependencies.Length, Allocator.Temp);
				for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
				{
					var dependencyGuid = dependencies[dependencyIndex];
					if (dependencyGuid != default)
					{
						var dependencyGuidString = dependencyGuid.ToString("N");
						var dep = LoadArchive(GetArchivePath(dependencyGuidString), dependencyGuidString);
						deps[dependencyIndex] = dep.contentFile;
					}
					else
					{
						deps[dependencyIndex] = ContentFile.GlobalTableDependency;
					}
				}

				var contentFilePath = archive.GetMountPath()+fileName;
				contentFile = ContentLoadInterface.LoadContentFileAsync(_contentNamespace, contentFilePath, deps);
				contentFile.WaitForCompletion(0);
				deps.Dispose();
			}

			var handle = new ExploreArchiveHandle(fileName, archive, contentFile);
			_localHandles.Add(handle);
			return handle;
		}

		void ProcessUnloads()
		{
			bool unloadChanged;
			do
			{
				unloadChanged = false;
				var toUnload = -1;
				foreach (var toUnloadName in _toUnload)
				{
					var index = _localHandles.FindIndex(handle => handle.fileName == toUnloadName);
					var handle = _localHandles[index];
					var guid = new SerializableGuid(handle.fileName);
					var canBeUnloaded = true;
					if (_prometheusMapping.ContentFile2Dependants.TryGetValue(guid, out var dependants))
					{
						foreach (var dependant in dependants)
						{
							var dependantGuidString = dependant.ToString("N");
							var dependantIndex = _localHandles.FindIndex(h => h.fileName == dependantGuidString);
							if (dependantIndex != -1)
							{
								canBeUnloaded = false;
								break;
							}
						}
					}
					if (canBeUnloaded)
					{
						toUnload = index;
						break;
					}
				}
				if (toUnload != -1)
				{
					var fileName = _localHandles[toUnload].fileName;
					_localHandles[toUnload].contentFile.UnloadAsync().WaitForCompletion(0);
					_localHandles[toUnload].archive.Unmount().Complete();
					_localHandles.RemoveAt(toUnload);
					_toUnload.Remove(fileName);
					unloadChanged = true;
				}
			}
			while (unloadChanged);
		}

		string GetArchivePath(string fileName)
		{
			return Path.Combine(PrometheusPersistence.ArchivesDirectoryPath, fileName);
		}

		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/Archive explorer Window", false, 90)]
		static void ShowWindow()
		{
			var window = GetWindow<PrometheusArchiveExplorerWindow>();
			window.titleContent = new("Archive explorer");
			window.Show();
		}

		readonly struct ExploreArchiveHandle : IEquatable<ExploreArchiveHandle>
		{
			public readonly string fileName;
			public readonly ArchiveHandle archive;
			public readonly ContentFile contentFile;

			public ExploreArchiveHandle(string fileName, ArchiveHandle archive, ContentFile contentFile)
			{
				this.fileName = fileName;
				this.archive = archive;
				this.contentFile = contentFile;
			}

			public bool Equals(ExploreArchiveHandle other)
			{
				return fileName == other.fileName;
			}
			public override bool Equals(object obj)
			{
				return obj is ExploreArchiveHandle other && Equals(other);
			}
			public override int GetHashCode()
			{
				return fileName.GetHashCode();
			}
			public static bool operator ==(ExploreArchiveHandle left, ExploreArchiveHandle right)
			{
				return left.Equals(right);
			}
			public static bool operator !=(ExploreArchiveHandle left, ExploreArchiveHandle right)
			{
				return !left.Equals(right);
			}
		}
	}
}
