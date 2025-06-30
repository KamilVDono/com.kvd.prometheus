using System;
using System.IO;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.Archive;
using Unity.Jobs;
using Unity.Loading;
using Unity.Mathematics;
using Unity.Profiling;

namespace KVD.Prometheus
{
	public unsafe partial class PrometheusLoader
	{
		public const ushort DefaultMaxOngoingMountingCount = 20;
		public const ushort DefaultMaxOngoingContentLoadingCount = 10;
		public const ushort DefaultMaxOngoingUnmountingCount = 20;
		public const ushort DefaultMaxOngoingContentUnloadingCount = 10;

		public static readonly ProfilerMarker UpdateFileManagementMarker = new("PrometheusLoader.UpdateFileManagement");
		public static readonly ProfilerMarker AllocationsUpdateFileManagementMarker = new("PrometheusLoader.UpdateFileManagement.Allocations");
		public static readonly ProfilerMarker PopulateUpdateFileManagementMarker = new("PrometheusLoader.UpdateFileManagement.Populate");
		public static readonly ProfilerMarker StateMachineUpdateFileManagementMarker = new("PrometheusLoader.UpdateFileManagement.StateMachine");
		public static readonly ProfilerMarker DisposesUpdateFileManagementMarker = new("PrometheusLoader.UpdateFileManagement.Disposes");

		Unmanaged _unmanaged;

		bool _fileManagedUpdatePaused;

		ushort _maxOngoingMountingCount;
		ushort _maxOngoingContentLoadingCount;
		ushort _maxOngoingUnmountingCount;
		ushort _maxOngoingContentUnloadingCount;

		/// <summary>
		/// Unmanaged API for PrometheusLoader. Be aware that this API is not thread-safe and should be used only from the main thread, but can be bursted
		/// </summary>
		public ref Unmanaged UnmanagedApi => ref _unmanaged;

		public struct Unmanaged
		{
			UnsafeHashMap<PrometheusIdentifier, SerializableGuid>* _asset2ContentFile;
			UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>>* _contentFile2Dependencies;

			internal NativeHashMap<SerializableGuid, uint> _contentFile2Index;

			internal OccupiedArray<ContentFileLoad> _contentFileLoads;

			internal byte _ongoingMountingCount;
			internal byte _ongoingContentLoadingCount;
			internal byte _ongoingUnmountingCount;
			internal byte _ongoingContentUnloadingCount;

#if UNITY_EDITOR
			UnsafeList<ScheduledOperation>* _editorScheduledOperations;
#endif

			public Unmanaged(PrometheusLoader loader, PrometheusMapping prometheusMapping)
			{
				_asset2ContentFile = (UnsafeHashMap<PrometheusIdentifier, SerializableGuid>*)UnsafeUtility.AddressOf(ref prometheusMapping.asset2ContentFile);
				_contentFile2Dependencies = (UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>>*)UnsafeUtility.AddressOf(ref prometheusMapping.contentFile2Dependencies);
				var contentFilesCount = (uint)math.max(prometheusMapping.contentFile2Dependencies.Count*0.1f, 100f);
				_contentFile2Index = new((int)contentFilesCount, Allocator.Domain);
				_contentFileLoads = new(contentFilesCount, Allocator.Domain);

				_ongoingMountingCount = 0;
				_ongoingContentLoadingCount = 0;
				_ongoingUnmountingCount = 0;
				_ongoingContentUnloadingCount = 0;

#if UNITY_EDITOR
				_editorScheduledOperations = (UnsafeList<ScheduledOperation>*)UnsafeUtility.AddressOf(ref loader._editorScheduledOperations);
#endif
			}

			public void StartAssetLoading(PrometheusIdentifier prometheusIdentifier, Priority priority = Priority.Normal)
			{
				StartAssetLoading(prometheusIdentifier, (byte)priority);
			}

			public void StartAssetLoading(PrometheusIdentifier prometheusIdentifier, byte priority)
			{
				if (!_asset2ContentFile->TryGetValue(prometheusIdentifier, out var contentFileGuid))
				{
#if UNITY_EDITOR
					_editorScheduledOperations->Add(new ScheduledOperation(prometheusIdentifier, priority, OperationType.Load));
#endif
					return;
				}

				StartLoading(contentFileGuid, priority);
			}

			public void StartAssetUnloading(PrometheusIdentifier prometheusIdentifier, Priority priority = Priority.Normal)
			{
				StartAssetUnloading(prometheusIdentifier, (byte)priority);
			}


			public void StartAssetUnloading(PrometheusIdentifier prometheusIdentifier, byte priority)
			{
				if (!_asset2ContentFile->TryGetValue(prometheusIdentifier, out var contentFileGuid))
				{
#if UNITY_EDITOR
					_editorScheduledOperations->Add(new ScheduledOperation(prometheusIdentifier, priority, OperationType.Load));
#endif
					return;
				}

				StartUnloading(contentFileGuid, priority);
			}

			internal uint StartLoading(SerializableGuid contentFileGuid, byte priority)
			{
				var alreadyRegistered = _contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex);
				if (alreadyRegistered)
				{
					ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
					loadedContentFile.referenceCount++;
					loadedContentFile.Priority = (byte)math.max((int)priority, (int)loadedContentFile.Priority);

					if (loadedContentFile.referenceCount > 1)
					{
						return loadingIndex;
					}
				}

				if (alreadyRegistered)
				{
					ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
					var state = loadedContentFile.State;

					loadedContentFile.ChangeRequest &= ~ChangeRequest.ToUnregister;

					if (state == State.WaitingToStartUnloading)
					{
						loadedContentFile.State = loadedContentFile.contentFile.LoadingStatus == LoadingStatus.Completed ? State.Loaded : State.ErrorArchive;
					}
					else if (state == State.Unloading)
					{
						loadedContentFile.ChangeRequest |= ChangeRequest.ToRegister;
					}
					else if (state == State.WaitingForUnmount)
					{
						loadedContentFile.State = State.WaitingForDependencies;
					}
					else if (state == State.Unmounting)
					{
						loadedContentFile.ChangeRequest |= ChangeRequest.ToRegister;
					}
				}
				else
				{
					var fileLoad = new ContentFileLoad
					{
						State = State.WaitingForMounting,
						contentFileGuid = contentFileGuid,
						referenceCount = 1,
					};
					fileLoad.Priority = priority;

					loadingIndex = _contentFileLoads.Insert(fileLoad);

					_contentFile2Index.Add(contentFileGuid, loadingIndex);
				}

				var requirements = (*_contentFile2Dependencies)[contentFileGuid];

				for (var i = 0; i < requirements.Length; i++)
				{
					var requirementGuid = requirements[i];
					if (requirementGuid != default)
					{
						StartLoading(requirementGuid, priority);
					}
				}

				return loadingIndex;
			}

			internal void StartUnloading(SerializableGuid contentFileGuid, byte priority)
			{
				var loadingIndex = _contentFile2Index[contentFileGuid];
				ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
				--loadedContentFile.referenceCount;

				if (loadedContentFile.referenceCount == 0)
				{
					loadedContentFile.Priority = (byte)math.max((int)priority, (int)loadedContentFile.Priority);
					loadedContentFile.ChangeRequest &= ~ChangeRequest.ToRegister;

					if (loadedContentFile.State == State.WaitingForMounting)
					{
						loadedContentFile.State = State.Unmounting;
						++_ongoingUnmountingCount;
					}
					else if (loadedContentFile.State == State.Mounting)
					{
						loadedContentFile.ChangeRequest |= ChangeRequest.ToUnregister;
					}
					else if (loadedContentFile.State == State.WaitingForDependencies)
					{
						loadedContentFile.State = State.WaitingForUnmount;
					}
					else if (loadedContentFile.State == State.Loading)
					{
						loadedContentFile.ChangeRequest |= ChangeRequest.ToUnregister;
					}
					else if (loadedContentFile.State == State.ErrorArchive)
					{
						loadedContentFile.State = State.WaitingForUnmount;
					}
					else if (loadedContentFile.State == State.ErrorContentFiles)
					{
						loadedContentFile.State = State.WaitingToStartUnloading;
					}
					else
					{
						loadedContentFile.State = State.WaitingToStartUnloading;
					}

					var requirements = (*_contentFile2Dependencies)[contentFileGuid];
					foreach (var requirement in requirements)
					{
						if (requirement != default)
						{
							StartUnloading(requirement, priority);
						}
					}
				}
			}
		}
		
		public void OverrideSettings(Setup setup)
		{
			_maxOngoingMountingCount = setup.maxOngoingMountingCount.GetValueOrDefault(_maxOngoingMountingCount);
			_maxOngoingContentLoadingCount = setup.maxOngoingContentLoadingCount.GetValueOrDefault(_maxOngoingContentLoadingCount);
			_maxOngoingUnmountingCount = setup.maxOngoingUnmountingCount.GetValueOrDefault(_maxOngoingUnmountingCount);
			_maxOngoingContentUnloadingCount = setup.maxOngoingContentUnloadingCount.GetValueOrDefault(_maxOngoingContentUnloadingCount);
		}

		void InitFilesManagement()
		{
			_unmanaged = new Unmanaged(this, _prometheusMapping);
			_maxOngoingMountingCount = DefaultMaxOngoingMountingCount;
			_maxOngoingContentLoadingCount = DefaultMaxOngoingContentLoadingCount;
			_maxOngoingUnmountingCount = DefaultMaxOngoingUnmountingCount;
			_maxOngoingContentUnloadingCount = DefaultMaxOngoingContentUnloadingCount;
		}

		void ForceLoad(ref ContentFileLoad load)
		{
			var requirements = _prometheusMapping.contentFile2Dependencies[load.contentFileGuid];
			var dependencies = new NativeArray<ContentFile>(requirements.LengthInt, Allocator.Temp);

			for (var i = 0; i < requirements.Length; i++)
			{
				var requirementGuid = requirements[i];
				if (requirementGuid != default)
				{
					var requirementIndex = _unmanaged._contentFile2Index[requirements[i]];
					ref var requirementLoad = ref _unmanaged._contentFileLoads[requirementIndex];
					if (!IsLoaded(requirementLoad))
					{
						ForceLoad(ref requirementLoad);
					}

					dependencies[i] = requirementLoad.contentFile;
				}
				else
				{
					dependencies[i] = ContentFile.GlobalTableDependency;
				}
			}

			var contentString = load.contentFileGuid.ToString("N");
			if (load.State == State.WaitingForMounting)
			{
				var archiveFilePath = Path.Combine(PrometheusPersistence.ArchivesDirectoryPath, contentString);
				load.archiveHandle = ArchiveFileInterface.MountAsync(_contentNamespace, archiveFilePath, string.Empty);
				load.archiveHandle.JobHandle.Complete();
				if (load.archiveHandle.Status == ArchiveStatus.Failed)
				{
					load.State = State.ErrorArchive;
					return;
				}
			}

			var contentFilePath = load.archiveHandle.GetMountPath()+contentString;
			load.contentFile = ContentLoadInterface.LoadContentFileAsync(_contentNamespace, contentFilePath, dependencies);
			load.contentFile.WaitForCompletion(0);

			dependencies.Dispose();

			if (load.contentFile.LoadingStatus == LoadingStatus.Failed)
			{
				load.State = State.ErrorContentFiles;
			}
			else if (load.contentFile.LoadingStatus == LoadingStatus.Completed)
			{
				load.State = State.Loaded;
			}
		}

		void UpdateFileManagement()
		{
			UpdateFileManagementMarker.Begin();

			if (_fileManagedUpdatePaused)
			{
				UpdateFileManagementMarker.End();
				return;
			}

			// -- Allocate
			AllocationsUpdateFileManagementMarker.Begin();

			var waitingForStartMountingList = new UnsafePriorityList<uint, byte>((uint)_maxOngoingMountingCount-_unmanaged._ongoingMountingCount, Allocator.Temp);
			var waitingForMountFinish = new UnsafeList<uint>(_maxOngoingMountingCount, Allocator.Temp);
			var waitingForDependenciesList = new UnsafePriorityList<uint, byte>((uint)_maxOngoingContentLoadingCount-_unmanaged._ongoingContentLoadingCount, Allocator.Temp);
			var loadingList = new UnsafeList<uint>(_maxOngoingContentLoadingCount, Allocator.Temp);
			var waitingForUnloadingList = new UnsafePriorityList<uint, byte>((uint)_maxOngoingContentUnloadingCount-_unmanaged._ongoingContentUnloadingCount, Allocator.Temp);
			var unloadingList = new UnsafeList<uint>(_maxOngoingUnmountingCount, Allocator.Temp);
			var unmountingList = new UnsafeList<uint>(_maxOngoingUnmountingCount, Allocator.Temp);
			var waitingForUnmountingStartList = new UnsafePriorityList<uint, byte>((uint)_maxOngoingUnmountingCount-_unmanaged._ongoingUnmountingCount, Allocator.Temp);

			var dependencies = new NativeList<ContentFile>(12, Allocator.Temp);

			AllocationsUpdateFileManagementMarker.End();

			// -- Populate
			PopulateUpdateFileManagementMarker.Begin();
			var populateJob = new PopulateUpdateDataJob
			{
				contentFileLoads = _unmanaged._contentFileLoads,
				waitingForStartMountingList = &waitingForStartMountingList,
				waitingForMountFinish = &waitingForMountFinish,
				waitingForDependenciesList = &waitingForDependenciesList,
				loadingList = &loadingList,
				waitingForUnloadingList = &waitingForUnloadingList,
				unloadingList = &unloadingList,
				unmountingList = &unmountingList,
				waitingForUnmountingStartList = &waitingForUnmountingStartList
			};
			populateJob.Run();
			PopulateUpdateFileManagementMarker.End();

			// -- State machine
			StateMachineUpdateFileManagementMarker.Begin();
			// Start mounting
			while (_unmanaged._ongoingMountingCount < _maxOngoingMountingCount && waitingForStartMountingList.Length > 0)
			{
				var loadIndex = waitingForStartMountingList.Pop();
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				var contentString = load.contentFileGuid.ToString("N");
				var archiveFilePath = Path.Combine(PrometheusPersistence.ArchivesDirectoryPath, contentString);
				var archive = ArchiveFileInterface.MountAsync(_contentNamespace, archiveFilePath, string.Empty);

				load.archiveHandle = archive;
				load.State = State.Mounting;

				++_unmanaged._ongoingMountingCount;
			}

			// Check if mounting is finished
			foreach (var loadIndex in waitingForMountFinish)
			{
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				if (load.archiveHandle.Status == ArchiveStatus.InProgress)
				{
					continue;
				}

				--_unmanaged._ongoingMountingCount;
				if (load.ChangeRequest.HasFlagFast(ChangeRequest.ToUnregister))
				{
					load.State = State.WaitingForUnmount;
					load.ChangeRequest &= ~ChangeRequest.ToUnregister;

					waitingForUnmountingStartList.Add(loadIndex, load.Priority);
				}
				else
				{
					if (load.archiveHandle.Status == ArchiveStatus.Failed)
					{
						load.State = State.ErrorArchive;
					}
					else if (load.archiveHandle.Status == ArchiveStatus.Complete)
					{
						load.State = State.WaitingForDependencies;

						waitingForDependenciesList.Add(loadIndex, load.Priority);
					}
				}
			}

			// Waiting for dependencies and start final loading
			while (_unmanaged._ongoingContentLoadingCount < _maxOngoingContentLoadingCount && waitingForDependenciesList.Length > 0)
			{
				var loadIndex = waitingForDependenciesList.Pop();
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				var requirements = _prometheusMapping.contentFile2Dependencies[load.contentFileGuid];
				var allDependenciesLoaded = true;
				for (var i = 0; i < requirements.Length; i++)
				{
					var requirementGuid = requirements[i];
					if (requirementGuid != default)
					{
						var requirementIndex = _unmanaged._contentFile2Index[requirementGuid];
						ref var requirementLoad = ref _unmanaged._contentFileLoads[requirementIndex];
						allDependenciesLoaded = requirementLoad.State is State.Loaded or State.ErrorArchive && allDependenciesLoaded;
						dependencies.Add(requirementLoad.contentFile);
					}
					else
					{
						dependencies.Add(ContentFile.GlobalTableDependency);
					}
				}

				if (allDependenciesLoaded)
				{
					var contentFilePath = load.archiveHandle.GetMountPath()+load.contentFileGuid.ToString("N");
					load.contentFile = ContentLoadInterface.LoadContentFileAsync(_contentNamespace, contentFilePath, dependencies.AsArray());
					load.State = State.Loading;

					loadingList.Add(loadIndex);

					++_unmanaged._ongoingContentLoadingCount;
				}

				dependencies.Clear();
			}

			// Check if loading is finished
			foreach (var loadingIndex in loadingList)
			{
				ref var load = ref _unmanaged._contentFileLoads[loadingIndex];

				if (load.contentFile.LoadingStatus == LoadingStatus.InProgress)
				{
					continue;
				}

				--_unmanaged._ongoingContentLoadingCount;
				if (load.ChangeRequest.HasFlagFast(ChangeRequest.ToUnregister))
				{
					load.State = State.WaitingToStartUnloading;
					load.ChangeRequest &= ~ChangeRequest.ToUnregister;

					waitingForUnloadingList.Add(loadingIndex, load.Priority);
				}
				else
				{
					if (load.contentFile.LoadingStatus == LoadingStatus.Failed)
					{
						load.State = State.ErrorContentFiles;
					}
					else if (load.contentFile.LoadingStatus == LoadingStatus.Completed)
					{
						load.State = State.Loaded;
					}
				}
			}

			// Check if can start unloading
			// To start unloading, all other content files, which depends on us, (dependants) must be unloaded
			while (_unmanaged._ongoingContentUnloadingCount < _maxOngoingContentUnloadingCount && waitingForUnloadingList.Length > 0)
			{
				var loadIndex = waitingForUnloadingList.Pop();
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				var allDependantsUnloaded = true;
				if (_prometheusMapping.contentFile2Dependants.TryGetValue(load.contentFileGuid, out var dependants))
				{
					for (var i = 0; i < dependants.Length && allDependantsUnloaded; i++)
					{
						if (_unmanaged._contentFile2Index.TryGetValue(dependants[i], out var dependantIndex))
						{
							ref var dependantLoad = ref _unmanaged._contentFileLoads[dependantIndex];
							allDependantsUnloaded = dependantLoad.State is State.WaitingForUnmount or State.Unmounting;
						}
					}
				}

				if (allDependantsUnloaded)
				{
					var unloadHandle = load.contentFile.UnloadAsync();
					load.unloadHandle = unloadHandle;
					load.State = State.Unloading;

					unloadingList.Add(loadIndex);

					++_unmanaged._ongoingContentUnloadingCount;
				}
			}

			// Check if unloading is finished
			foreach (var loadIndex in unloadingList)
			{
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				if (!load.unloadHandle.IsCompleted)
				{
					continue;
				}

				if (load.ChangeRequest.HasFlagFast(ChangeRequest.ToRegister))
				{
					load.State = State.WaitingForDependencies;
					load.ChangeRequest &= ~ChangeRequest.ToRegister;
				}
				else
				{
					load.State = State.WaitingForUnmount;

					waitingForUnmountingStartList.Add(loadIndex, load.Priority);
				}

				--_unmanaged._ongoingContentUnloadingCount;
			}

			// Clean up unmounted, there is no check as unmounting is JobHandle and there is no reliable way to check if it is completed
			// Just assume that at the next frame it will be completed
			foreach (var loadIndex in unmountingList)
			{
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				if (load.ChangeRequest.HasFlagFast(ChangeRequest.ToRegister))
				{
					load.State = State.WaitingForMounting;
					load.ChangeRequest &= ~ChangeRequest.ToRegister;
				}
				else
				{
					_unmanaged._contentFile2Index.Remove(load.contentFileGuid);
					_unmanaged._contentFileLoads.Release(loadIndex);
				}

				--_unmanaged._ongoingUnmountingCount;
			}

			// Start unmounting
			while (_unmanaged._ongoingUnmountingCount < _maxOngoingUnmountingCount && waitingForUnmountingStartList.Length > 0)
			{
				var loadIndex = waitingForUnmountingStartList.Pop();
				ref var load = ref _unmanaged._contentFileLoads[loadIndex];

				load.archiveHandle.Unmount();
				load.State = State.Unmounting;
				++_unmanaged._ongoingUnmountingCount;
			}
			StateMachineUpdateFileManagementMarker.End();

			// -- Cleanup
			DisposesUpdateFileManagementMarker.Begin();
			waitingForStartMountingList.Dispose();
			waitingForMountFinish.Dispose();
			waitingForDependenciesList.Dispose();
			loadingList.Dispose();
			waitingForUnloadingList.Dispose();
			unloadingList.Dispose();
			unmountingList.Dispose();
			waitingForUnmountingStartList.Dispose();
			dependencies.Dispose();
			DisposesUpdateFileManagementMarker.End();

			UpdateFileManagementMarker.End();
		}

		public struct ContentFileLoad
		{
			// 16 bits
			// 4 - State
			// 2 - Change request
			// 8 - Priority
			// 2 - Unused
			public int compressedData;
			public int referenceCount;
			public SerializableGuid contentFileGuid;

			public ContentFile contentFile;
			public ArchiveHandle archiveHandle;
			public ContentFileUnloadHandle unloadHandle;

			public State State
			{
				get => (State)(byte)(compressedData & 0b1111);
				set => compressedData = (compressedData & ~0b1111) | (byte)value;
			}

			public ChangeRequest ChangeRequest
			{
				get => (ChangeRequest)(byte)((compressedData >> 4) & 0b11);
				set => compressedData = (compressedData & ~(0b11 << 4)) | ((byte)value << 4);
			}

			public byte Priority
			{
				get => (byte)((compressedData >> 6) & 0b1111_1111);
				set => compressedData = (compressedData & ~(0b1111_1111 << 6)) | (value << 6);
			}

			public void Deconstruct(out ContentFile contentFile, out ArchiveHandle archiveHandle)
			{
				contentFile = this.contentFile;
				archiveHandle = this.archiveHandle;
			}
		}

		public enum State : byte
		{
			None = 0,

			WaitingForMounting = 1,
			Mounting = 2,
			WaitingForDependencies = 3,
			Loading = 4,

			Loaded = 5,
			ErrorArchive = 6,
			ErrorContentFiles = 7,

			WaitingToStartUnloading = 8,
			Unloading = 9,
			WaitingForUnmount = 10,
			Unmounting = 11,
		}

		[Flags]
		public enum ChangeRequest : byte
		{
			None = 0,

			ToRegister = 1 << 0,
			ToUnregister = 1 << 1,
		}

		// TODO: Burst makes it yield invalid data, in UnsafePriorityList there are duplicated entries and presumable some other are missing
		//[BurstCompile]
		struct PopulateUpdateDataJob : IJob
		{
			public OccupiedArray<ContentFileLoad> contentFileLoads;

			[NativeDisableUnsafePtrRestriction] public UnsafePriorityList<uint, byte>* waitingForStartMountingList;
			[NativeDisableUnsafePtrRestriction] public UnsafeList<uint>* waitingForMountFinish;
			[NativeDisableUnsafePtrRestriction] public UnsafePriorityList<uint, byte>* waitingForDependenciesList;
			[NativeDisableUnsafePtrRestriction] public UnsafeList<uint>* loadingList;
			[NativeDisableUnsafePtrRestriction] public UnsafePriorityList<uint, byte>* waitingForUnloadingList;
			[NativeDisableUnsafePtrRestriction] public UnsafeList<uint>* unloadingList;
			[NativeDisableUnsafePtrRestriction] public UnsafeList<uint>* unmountingList;
			[NativeDisableUnsafePtrRestriction] public UnsafePriorityList<uint, byte>* waitingForUnmountingStartList;

			public void Execute()
			{
				foreach (var (loadPtr, index) in contentFileLoads.EnumerateOccupiedIndexed())
				{
					ref readonly var load = ref *loadPtr;
					if (load.State == State.WaitingForMounting)
					{
						waitingForStartMountingList->Add(index, load.Priority);
					}
					else if (load.State == State.Mounting)
					{
						waitingForMountFinish->Add(index);
					}
					else if (load.State == State.WaitingForDependencies)
					{
						waitingForDependenciesList->Add(index, load.Priority);
					}
					else if (load.State == State.Loading)
					{
						loadingList->Add(index);
					}
					else if (load.State == State.WaitingToStartUnloading)
					{
						waitingForUnloadingList->Add(index, load.Priority);
					}
					else if (load.State == State.Unloading)
					{
						unloadingList->Add(index);
					}
					else if (load.State == State.Unmounting)
					{
						unmountingList->Add(index);
					}
					else if (load.State == State.WaitingForUnmount)
					{
						waitingForUnmountingStartList->Add(index, load.Priority);
					}
				}
			}
		}

		public struct Setup
		{
			public Option<ushort> maxOngoingMountingCount;
			public Option<ushort> maxOngoingContentLoadingCount;
			public Option<ushort> maxOngoingUnmountingCount;
			public Option<ushort> maxOngoingContentUnloadingCount;

			public static Setup Default => new Setup
			{
				maxOngoingMountingCount = DefaultMaxOngoingMountingCount,
				maxOngoingContentLoadingCount = DefaultMaxOngoingContentLoadingCount,
				maxOngoingUnmountingCount = DefaultMaxOngoingUnmountingCount,
				maxOngoingContentUnloadingCount = DefaultMaxOngoingContentUnloadingCount
			};

			public static Setup Empty => new Setup
			{
				maxOngoingMountingCount = Option<ushort>.None,
				maxOngoingContentLoadingCount = Option<ushort>.None,
				maxOngoingUnmountingCount = Option<ushort>.None,
				maxOngoingContentUnloadingCount = Option<ushort>.None
			};

			public Setup(Option<ushort> maxOngoingMountingCount, Option<ushort> maxOngoingContentLoadingCount, Option<ushort> maxOngoingUnmountingCount, Option<ushort> maxOngoingContentUnloadingCount)
			{
				this.maxOngoingMountingCount = maxOngoingMountingCount;
				this.maxOngoingContentLoadingCount = maxOngoingContentLoadingCount;
				this.maxOngoingUnmountingCount = maxOngoingUnmountingCount;
				this.maxOngoingContentUnloadingCount = maxOngoingContentUnloadingCount;
			}
		}
	}

	public static class SetupBuilder
	{
		public static PrometheusLoader.Setup WithMaxOngoingMountingCount(this in PrometheusLoader.Setup setup, ushort maxOngoingMountingCount)
		{
			return new PrometheusLoader.Setup
			{
				maxOngoingMountingCount = maxOngoingMountingCount,
				maxOngoingContentLoadingCount = setup.maxOngoingContentLoadingCount,
				maxOngoingUnmountingCount = setup.maxOngoingUnmountingCount,
				maxOngoingContentUnloadingCount = setup.maxOngoingContentUnloadingCount
			};
		}

		public static PrometheusLoader.Setup WithMaxOngoingContentLoadingCount(this in PrometheusLoader.Setup setup, ushort maxOngoingContentLoadingCount)
		{
			return new PrometheusLoader.Setup
			{
				maxOngoingMountingCount = setup.maxOngoingMountingCount,
				maxOngoingContentLoadingCount = maxOngoingContentLoadingCount,
				maxOngoingUnmountingCount = setup.maxOngoingUnmountingCount,
				maxOngoingContentUnloadingCount = setup.maxOngoingContentUnloadingCount
			};
		}

		public static PrometheusLoader.Setup WithMaxOngoingUnmountingCount(this in PrometheusLoader.Setup setup, ushort maxOngoingUnmountingCount)
		{
			return new PrometheusLoader.Setup
			{
				maxOngoingMountingCount = setup.maxOngoingMountingCount,
				maxOngoingContentLoadingCount = setup.maxOngoingContentLoadingCount,
				maxOngoingUnmountingCount = maxOngoingUnmountingCount,
				maxOngoingContentUnloadingCount = setup.maxOngoingContentUnloadingCount
			};
		}

		public static PrometheusLoader.Setup WithMaxOngoingContentUnloadingCount(this in PrometheusLoader.Setup setup, ushort maxOngoingContentUnloadingCount)
		{
			return new PrometheusLoader.Setup
			{
				maxOngoingMountingCount = setup.maxOngoingMountingCount,
				maxOngoingContentLoadingCount = setup.maxOngoingContentLoadingCount,
				maxOngoingUnmountingCount = setup.maxOngoingUnmountingCount,
				maxOngoingContentUnloadingCount = maxOngoingContentUnloadingCount
			};
		}
	}
}
