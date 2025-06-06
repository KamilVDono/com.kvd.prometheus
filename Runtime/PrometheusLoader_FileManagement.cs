using System;
using System.IO;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.Archive;
using Unity.Loading;
using Unity.Mathematics;

namespace KVD.Prometheus
{
	public unsafe partial class PrometheusLoader
	{
		const byte MaxOngoingMountingCount = 20;
		const byte MaxOngoingContentLoadingCount = 10;
		const byte MaxOngoingUnmountingCount = 20;
		const byte MaxOngoingContentUnloadingCount = 10;

		NativeHashMap<SerializableGuid, uint> _contentFile2Index;

		OccupiedArray<ContentFileLoad> _contentFileLoads;

		byte _ongoingMountingCount;
		byte _ongoingContentLoadingCount;
		byte _ongoingUnmountingCount;
		byte _ongoingContentUnloadingCount;

		bool _fileManagedUpdatePaused;

		void InitFileManagement()
		{
			var contentFilesCount = (uint)math.max(_prometheusMapping.ContentFile2Dependencies.Count * 0.1f, 100f);
			_contentFile2Index = new((int)contentFilesCount, Allocator.Domain);
			_contentFileLoads = new(contentFilesCount, Allocator.Domain);
		}

		uint StartLoading(SerializableGuid contentFileGuid, byte priority)
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

			var requirements = _prometheusMapping.ContentFile2Dependencies[contentFileGuid];

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

		void StartUnloading(SerializableGuid contentFileGuid, byte priority)
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

				var requirements = _prometheusMapping.ContentFile2Dependencies[contentFileGuid];
				foreach (var requirement in requirements)
				{
					if (requirement != default)
					{
						StartUnloading(requirement, priority);
					}
				}
			}
		}

		void ForceLoad(ref ContentFileLoad load)
		{
			var requirements = _prometheusMapping.ContentFile2Dependencies[load.contentFileGuid];
			var dependencies = new NativeArray<ContentFile>(requirements.Length, Allocator.Temp);

			for (var i = 0; i < requirements.Length; i++)
			{
				var requirementGuid = requirements[i];
				if (requirementGuid != default)
				{
					var requirementIndex = _contentFile2Index[requirements[i]];
					ref var requirementLoad = ref _contentFileLoads[requirementIndex];
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
			if (_fileManagedUpdatePaused)
			{
				return;
			}

			// -- Allocate
			var waitingForStartMountingList = new UnsafePriorityList<uint, byte>((uint)MaxOngoingMountingCount-_ongoingMountingCount, Allocator.Temp);

			var waitingForMountFinish = new UnsafeList<uint>(MaxOngoingMountingCount, Allocator.Temp);

			var waitingForDependenciesList = new UnsafePriorityList<uint, byte>((uint)MaxOngoingContentLoadingCount-_ongoingContentLoadingCount, Allocator.Temp);

			var loadingList = new UnsafeList<uint>(MaxOngoingContentLoadingCount, Allocator.Temp);

			var waitingForUnloadingList = new UnsafePriorityList<uint, byte>((uint)MaxOngoingContentUnloadingCount-_ongoingContentUnloadingCount, Allocator.Temp);

			var unloadingList = new UnsafeList<uint>(MaxOngoingUnmountingCount, Allocator.Temp);

			var unmountingList = new UnsafeList<uint>(MaxOngoingUnmountingCount, Allocator.Temp);

			var waitingForUnmountingStartList = new UnsafePriorityList<uint, byte>((uint)MaxOngoingUnmountingCount-_ongoingUnmountingCount, Allocator.Temp);

			var dependencies = new NativeList<ContentFile>(12, Allocator.Temp);

			// -- Populate
			foreach (var (loadPtr, index) in _contentFileLoads.EnumerateOccupiedIndexed())
			{
				ref var load = ref *loadPtr;
				if (load.State == State.WaitingForMounting)
				{
					waitingForStartMountingList.Add(index, load.Priority);
				}

				if (load.State == State.Mounting)
				{
					waitingForMountFinish.Add(index);
				}

				if (load.State == State.WaitingForDependencies)
				{
					waitingForDependenciesList.Add(index, load.Priority);
				}

				if (load.State == State.Loading)
				{
					loadingList.Add(index);
				}

				if (load.State == State.WaitingToStartUnloading)
				{
					waitingForUnloadingList.Add(index, load.Priority);
				}

				if (load.State == State.Unloading)
				{
					unloadingList.Add(index);
				}

				if (load.State == State.Unmounting)
				{
					unmountingList.Add(index);
				}

				if (load.State == State.WaitingForUnmount)
				{
					waitingForUnmountingStartList.Add(index, load.Priority);
				}
			}

			// -- State machine
			// Start mounting
			while (_ongoingMountingCount < MaxOngoingMountingCount && waitingForStartMountingList.Length > 0)
			{
				var loadIndex = waitingForStartMountingList.Pop();
				ref var load = ref _contentFileLoads[loadIndex];

				var contentString = load.contentFileGuid.ToString("N");
				var archiveFilePath = Path.Combine(PrometheusPersistence.ArchivesDirectoryPath, contentString);
				var archive = ArchiveFileInterface.MountAsync(_contentNamespace, archiveFilePath, string.Empty);

				load.archiveHandle = archive;
				load.State = State.Mounting;

				++_ongoingMountingCount;
			}

			// Check if mounting is finished
			foreach (var loadIndex in waitingForMountFinish)
			{
				ref var load = ref _contentFileLoads[loadIndex];

				if (load.archiveHandle.Status == ArchiveStatus.InProgress)
				{
					continue;
				}

				--_ongoingMountingCount;
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
			while (_ongoingContentLoadingCount < MaxOngoingContentLoadingCount && waitingForDependenciesList.Length > 0)
			{
				var loadIndex = waitingForDependenciesList.Pop();
				ref var load = ref _contentFileLoads[loadIndex];

				var requirements = _prometheusMapping.ContentFile2Dependencies[load.contentFileGuid];
				var allDependenciesLoaded = true;
				for (var i = 0; i < requirements.Length; i++)
				{
					var requirementGuid = requirements[i];
					if (requirementGuid != default)
					{
						var requirementIndex = _contentFile2Index[requirementGuid];
						ref var requirementLoad = ref _contentFileLoads[requirementIndex];
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

					++_ongoingContentLoadingCount;
				}

				dependencies.Clear();
			}

			// Check if loading is finished
			foreach (var loadingIndex in loadingList)
			{
				ref var load = ref _contentFileLoads[loadingIndex];

				if (load.contentFile.LoadingStatus == LoadingStatus.InProgress)
				{
					continue;
				}

				--_ongoingContentLoadingCount;
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
			while (_ongoingContentUnloadingCount < MaxOngoingContentUnloadingCount && waitingForUnloadingList.Length > 0)
			{
				var loadIndex = waitingForUnloadingList.Pop();
				ref var load = ref _contentFileLoads[loadIndex];

				var allDependantsUnloaded = true;
				if (_prometheusMapping.ContentFile2Dependants.TryGetValue(load.contentFileGuid, out var dependants))
				{
					for (var i = 0; i < dependants.Length && allDependantsUnloaded; i++)
					{
						if (_contentFile2Index.TryGetValue(dependants[i], out var dependantIndex))
						{
							ref var dependantLoad = ref _contentFileLoads[dependantIndex];
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

					++_ongoingContentUnloadingCount;
				}
			}

			// Check if unloading is finished
			foreach (var loadIndex in unloadingList)
			{
				ref var load = ref _contentFileLoads[loadIndex];

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

				--_ongoingContentUnloadingCount;
			}

			// Clean up unmounted, there is no check as unmounting is JobHandle and there is no reliable way to check if it is completed
			// Just assume that at the next frame it will be completed
			foreach (var loadIndex in unmountingList)
			{
				ref var load = ref _contentFileLoads[loadIndex];

				if (load.ChangeRequest.HasFlagFast(ChangeRequest.ToRegister))
				{
					load.State = State.WaitingForMounting;
					load.ChangeRequest &= ~ChangeRequest.ToRegister;
				}
				else
				{
					_contentFile2Index.Remove(load.contentFileGuid);
					_contentFileLoads.Release(loadIndex);
				}

				--_ongoingUnmountingCount;
			}

			// Start unmounting
			while (_ongoingUnmountingCount < MaxOngoingUnmountingCount && waitingForUnmountingStartList.Length > 0)
			{
				var loadIndex = waitingForUnmountingStartList.Pop();
				ref var load = ref _contentFileLoads[loadIndex];

				load.archiveHandle.Unmount();
				load.State = State.Unmounting;
				++_ongoingUnmountingCount;
			}

			// -- Cleanup
			waitingForStartMountingList.Dispose();
			waitingForMountFinish.Dispose();
			waitingForDependenciesList.Dispose();
			loadingList.Dispose();
			waitingForUnloadingList.Dispose();
			unloadingList.Dispose();
			unmountingList.Dispose();
			waitingForUnmountingStartList.Dispose();
			dependencies.Dispose();
		}

		bool IsLoaded(in ContentFileLoad load)
		{
			return load.State is State.Loaded or State.ErrorArchive or State.ErrorContentFiles;
		}

		bool IsSuccessfullyLoaded(in ContentFileLoad load)
		{
			return load.State is State.Loaded;
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
	}
}
