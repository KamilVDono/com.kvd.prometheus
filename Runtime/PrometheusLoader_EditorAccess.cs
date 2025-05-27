using System.IO;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Content;
using Unity.IO.Archive;
using Unity.Loading;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		public struct EditorAccess
		{
			readonly PrometheusLoader _loader;

			public PrometheusMapping PrometheusMapping => _loader._prometheusMapping;
			public ContentNamespace ContentNamespace => _loader._contentNamespace;

			public byte OngoingMountingCount => _loader._ongoingMountingCount;
			public byte OngoingContentLoadingCount => _loader._ongoingContentLoadingCount;
			public byte OngoingUnmountingCount => _loader._ongoingUnmountingCount;
			public byte OngoingContentUnloadingCount => _loader._ongoingContentUnloadingCount;

			public ref NativeHashMap<SerializableGuid, uint> ContentFile2Index => ref _loader._contentFile2Index;
			public ref UnsafeArray<ContentFileLoad> ContentFileLoads => ref _loader._contentFileLoads;
			public ref UnsafeBitmask OccupiedContentFileIndices => ref _loader._occupiedSlots;
			public ref UnsafeBitmask ToRegister => ref _loader._toRegister;
			public ref UnsafeBitmask ToUnregister => ref _loader._toUnregister;

			public ref bool FileManagedPaused => ref _loader._fileManagedUpdatePaused;

			public ref Callback[] Callbacks => ref _loader._callbacks;
			public ref UnsafeArray<LoadingTaskData> LoadingTasks => ref _loader._loadingTasks;
			public ref UnsafeArray<byte> LoadingTasksVersion => ref _loader._loadingTasksVersion;
			public ref UnsafeBitmask LoadingTasksMask => ref _loader._loadingTasksMask;

			public EditorAccess(PrometheusLoader loader)
			{
				_loader = loader;
			}

			public ContentFile ForceLoaded(SerializableGuid contentFileGuid)
			{
				if (!_loader._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
				{
					loadingIndex = _loader.StartLoading(contentFileGuid);
				}
				ref var load = ref ContentFileLoads[loadingIndex];
				_loader.ForceLoad(ref load);

				return load.contentFile;
			}

			public string MountPath(SerializableGuid contentFileGuid)
			{
				if (!_loader._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
				{
					loadingIndex = _loader.StartLoading(contentFileGuid);
				}

				var load = ContentFileLoads[loadingIndex];
				if (load.state == State.WaitingForMounting)
				{
					var archiveFilePath = Path.Combine(PrometheusPersistence.ArchivesDirectoryPath, contentFileGuid.ToString("N"));
					load.archiveHandle = ArchiveFileInterface.MountAsync(ContentNamespace, archiveFilePath, string.Empty);
					load.archiveHandle.JobHandle.Complete();
				}

				return load.archiveHandle.GetMountPath();
			}
		}
	}
}
