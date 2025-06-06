﻿using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Content;

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
			public ref OccupiedArray<ContentFileLoad> ContentFileLoads => ref _loader._contentFileLoads;

			public ref bool FileManagedPaused => ref _loader._fileManagedUpdatePaused;

			public ref Callback[] Callbacks => ref _loader._callbacks;
			public ref UnsafeArray<LoadingTaskData> LoadingTasks => ref _loader._loadingTasks;
			public ref UnsafeArray<byte> LoadingTasksVersion => ref _loader._loadingTasksVersion;
			public ref UnsafeBitmask LoadingTasksMask => ref _loader._loadingTasksMask;

			public EditorAccess(PrometheusLoader loader)
			{
				_loader = loader;
			}
		}
	}
}
