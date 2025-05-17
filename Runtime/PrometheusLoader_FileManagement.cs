using System.IO;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.IO.Archive;
using Unity.Loading;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		const byte MaxOngoingMountingCount = 20;
		const byte MaxOngoingContentLoadingCount = 10;
		const byte MaxOngoingUnmountingCount = 20;
		const byte MaxOngoingContentUnloadingCount = 10;

		NativeHashMap<SerializableGuid, uint> _contentFile2Index;
		UnsafeArray<ContentFileLoad> _contentFileLoads;

		UnsafeBitmask _occupiedSlots;
		UnsafeBitmask _toRegister;
		UnsafeBitmask _toUnregister;

		byte _ongoingMountingCount;
		byte _ongoingContentLoadingCount;
		byte _ongoingUnmountingCount;
		byte _ongoingContentUnloadingCount;

		bool _fileManagedUpdatePaused;

		void InitFileManagement()
		{
			var contentFilesCount = (uint)_prometheusMapping.ContentFile2Dependencies.Count;
			_contentFile2Index = new((int)contentFilesCount, Allocator.Domain);
			_contentFileLoads = new(contentFilesCount, Allocator.Domain);
			_occupiedSlots = new(contentFilesCount, Allocator.Domain);
			_toRegister = new(contentFilesCount, Allocator.Domain);
			_toUnregister = new(contentFilesCount, Allocator.Domain);
		}

		uint StartLoading(SerializableGuid contentFileGuid)
		{
			var alreadyRegistered = _contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex);
			if (alreadyRegistered)
			{
				ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
				loadedContentFile.referenceCount++;

				if (loadedContentFile.referenceCount > 1)
				{
					return loadingIndex;
				}
			}

			if (alreadyRegistered)
			{
				ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
				var state = loadedContentFile.state;

				_toUnregister.Down(loadingIndex);

				if (state == State.WaitingToStartUnloading)
				{
					loadedContentFile.state = loadedContentFile.contentFile.LoadingStatus == LoadingStatus.Completed ? State.Loaded : State.ErrorArchive;
				}
				else if (state == State.Unloading)
				{
					_toRegister.Up(loadingIndex);
				}
				else if (state == State.WaitingForUnmount)
				{
					loadedContentFile.state = State.WaitingForDependencies;
				}
				else if (state == State.Unmounting)
				{
					_toRegister.Up(loadingIndex);
				}
			}
			else
			{
				loadingIndex = (uint)_occupiedSlots.FirstZero();
				_occupiedSlots.Up(loadingIndex);

				_contentFileLoads[loadingIndex] = new()
				{
					state = State.WaitingForMounting,
					contentFileGuid = contentFileGuid,
					referenceCount = 1,
				};
				_contentFile2Index.Add(contentFileGuid, loadingIndex);
			}

			var requirements = _prometheusMapping.ContentFile2Dependencies[contentFileGuid];

			for (var i = 0; i < requirements.Length; i++)
			{
				var requirementGuid = requirements[i];
				if (requirementGuid != default)
				{
					StartLoading(requirementGuid);
				}
			}

			return loadingIndex;
		}

		void StartUnloading(SerializableGuid contentFileGuid)
		{
			var loadingIndex = _contentFile2Index[contentFileGuid];
			ref var loadedContentFile = ref _contentFileLoads[loadingIndex];
			--loadedContentFile.referenceCount;

			if (loadedContentFile.referenceCount == 0)
			{
				_toRegister.Down(loadingIndex);

				if (loadedContentFile.state == State.WaitingForMounting)
				{
					loadedContentFile.state = State.Unmounting;
					++_ongoingUnmountingCount;
				}
				else if (loadedContentFile.state == State.Mounting)
				{
					_toUnregister.Up(loadingIndex);
				}
				else if (loadedContentFile.state == State.WaitingForDependencies)
				{
					loadedContentFile.state = State.WaitingForUnmount;
				}
				else if (loadedContentFile.state == State.Loading)
				{
					_toUnregister.Up(loadingIndex);
				}
				else if (loadedContentFile.state == State.ErrorArchive)
				{
					loadedContentFile.state = State.WaitingForUnmount;
				}
				else if (loadedContentFile.state == State.ErrorContentFiles)
				{
					loadedContentFile.state = State.WaitingToStartUnloading;
				}
				else
				{
					loadedContentFile.state = State.WaitingToStartUnloading;
				}

				var requirements = _prometheusMapping.ContentFile2Dependencies[contentFileGuid];
				foreach (var requirement in requirements)
				{
					if (requirement != default)
					{
						StartUnloading(requirement);
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
			if (load.state == State.WaitingForMounting)
			{
				var archiveFilePath = Path.Combine(PrometheusArchivesPath, contentString);
				load.archiveHandle = ArchiveFileInterface.MountAsync(_contentNamespace, archiveFilePath, string.Empty);
				load.archiveHandle.JobHandle.Complete();
				if (load.archiveHandle.Status == ArchiveStatus.Failed)
				{
					load.state = State.ErrorArchive;
					return;
				}
			}

			var contentFilePath = load.archiveHandle.GetMountPath()+contentString;
			load.contentFile = ContentLoadInterface.LoadContentFileAsync(_contentNamespace, contentFilePath, dependencies);
			load.contentFile.WaitForCompletion(0);

			dependencies.Dispose();

			if (load.contentFile.LoadingStatus == LoadingStatus.Failed)
			{
				load.state = State.ErrorContentFiles;
			}
			else if (load.contentFile.LoadingStatus == LoadingStatus.Completed)
			{
				load.state = State.Loaded;
			}
		}

		void UpdateFileManagement()
		{
			if (_fileManagedUpdatePaused)
			{
				return;
			}

			// Start mounting
			if (_ongoingMountingCount < MaxOngoingMountingCount)
			{
				foreach (var indexToMount in _occupiedSlots.EnumerateOnes())
				{
					ref var load = ref _contentFileLoads[indexToMount];
					if (load.state != State.WaitingForMounting)
					{
						continue;
					}

					var contentString = load.contentFileGuid.ToString("N");
					var archiveFilePath = Path.Combine(PrometheusArchivesPath, contentString);
					var archive = ArchiveFileInterface.MountAsync(_contentNamespace, archiveFilePath, string.Empty);
					load.archiveHandle = archive;
					load.state = State.Mounting;

					if (++_ongoingMountingCount == MaxOngoingMountingCount)
					{
						break;
					}
				}
			}

			// Start loading
			foreach (uint indexToFinishMount in _occupiedSlots.EnumerateOnes())
			{
				ref var load = ref _contentFileLoads[indexToFinishMount];
				if (load.state != State.Mounting)
				{
					continue;
				}

				if (load.archiveHandle.Status == ArchiveStatus.InProgress)
				{
					continue;
				}

				--_ongoingMountingCount;
				if (_toUnregister[indexToFinishMount])
				{
					load.state = State.WaitingForUnmount;
					_toUnregister.Down(indexToFinishMount);
				}
				else
				{
					if (load.archiveHandle.Status == ArchiveStatus.Failed)
					{
						load.state = State.ErrorArchive;
					}
					else if (load.archiveHandle.Status == ArchiveStatus.Complete)
					{
						load.state = State.WaitingForDependencies;
					}
				}
			}

			// Start content files
			if (_ongoingContentLoadingCount < MaxOngoingContentLoadingCount)
			{
				var dependencies = new NativeList<ContentFile>(12, Allocator.Temp);
				foreach (var indexToLoad in _occupiedSlots.EnumerateOnes())
				{
					ref var load = ref _contentFileLoads[indexToLoad];
					if (load.state != State.WaitingForDependencies)
					{
						continue;
					}

					var requirements = _prometheusMapping.ContentFile2Dependencies[load.contentFileGuid];
					var allDependenciesLoaded = true;
					for (var i = 0; i < requirements.Length; i++)
					{
						var requirementGuid = requirements[i];
						if (requirementGuid != default)
						{
							var requirementIndex = _contentFile2Index[requirementGuid];
							ref var requirementLoad = ref _contentFileLoads[requirementIndex];
							allDependenciesLoaded = requirementLoad.state is State.Loaded or State.ErrorArchive &&
							                        allDependenciesLoaded;
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
						load.contentFile = ContentLoadInterface.LoadContentFileAsync(_contentNamespace, contentFilePath,
							dependencies.AsArray());
						load.state = State.Loading;

						if (++_ongoingContentLoadingCount == MaxOngoingContentLoadingCount)
						{
							dependencies.Clear();
							break;
						}
					}

					dependencies.Clear();
				}

				dependencies.Dispose();
			}

			// Finish loading
			foreach (uint indexToFinishLoading in _occupiedSlots.EnumerateOnes())
			{
				ref var load = ref _contentFileLoads[indexToFinishLoading];
				if (load.state != State.Loading)
				{
					continue;
				}

				if (load.contentFile.LoadingStatus == LoadingStatus.InProgress)
				{
					continue;
				}

				--_ongoingContentLoadingCount;
				if (_toUnregister[indexToFinishLoading])
				{
					load.state = State.WaitingToStartUnloading;
					_toUnregister.Down(indexToFinishLoading);
				}
				else
				{
					if (load.contentFile.LoadingStatus == LoadingStatus.Failed)
					{
						load.state = State.ErrorContentFiles;
					}
					else if (load.contentFile.LoadingStatus == LoadingStatus.Completed)
					{
						load.state = State.Loaded;
					}
				}
			}

			// Start unloading
			if (_ongoingContentUnloadingCount < MaxOngoingContentUnloadingCount)
			{
				foreach (var indexToUnload in _occupiedSlots.EnumerateOnes())
				{
					ref var load = ref _contentFileLoads[indexToUnload];
					if (load.state != State.WaitingToStartUnloading)
					{
						continue;
					}

					var allDependantsUnloaded = true;
					if (_prometheusMapping.ContentFile2Dependants.TryGetValue(load.contentFileGuid,
						    out var dependants))
					{
						for (var i = 0; i < dependants.Length; i++)
						{
							if (_contentFile2Index.TryGetValue(dependants[i], out var dependantIndex))
							{
								ref var dependantLoad = ref _contentFileLoads[dependantIndex];
								allDependantsUnloaded =
									dependantLoad.state is State.WaitingForUnmount or State.Unmounting &&
									allDependantsUnloaded;
							}
						}
					}

					if (allDependantsUnloaded)
					{
						var unloadHandle = load.contentFile.UnloadAsync();
						load.unloadHandle = unloadHandle;
						load.state = State.Unloading;

						if (++_ongoingContentUnloadingCount == MaxOngoingContentUnloadingCount)
						{
							break;
						}
					}
				}
			}

			// Finish unloading
			foreach (var indexToFinishUnloading in _occupiedSlots.EnumerateOnes())
			{
				ref var load = ref _contentFileLoads[indexToFinishUnloading];
				if (load.state != State.Unloading)
				{
					continue;
				}

				if (load.unloadHandle.IsCompleted)
				{
					if (_toRegister[indexToFinishUnloading])
					{
						load.state = State.WaitingForDependencies;
						_toRegister.Down(indexToFinishUnloading);
					}
					else
					{
						load.state = State.WaitingForUnmount;
					}

					--_ongoingContentUnloadingCount;
				}
			}

			// Finish unmounting
			foreach (var indexToFinishUnmounting in _occupiedSlots.EnumerateOnes())
			{
				ref var load = ref _contentFileLoads[indexToFinishUnmounting];
				if (load.state != State.Unmounting)
				{
					continue;
				}

				if (_toRegister[indexToFinishUnmounting])
				{
					load.state = State.WaitingForMounting;
					_toRegister.Down(indexToFinishUnmounting);
				}
				else
				{
					_occupiedSlots.Down(indexToFinishUnmounting);
					_contentFile2Index.Remove(load.contentFileGuid);
					load = default;
				}

				--_ongoingUnmountingCount;
			}

			// Start unmounting
			if (_ongoingUnmountingCount < MaxOngoingUnmountingCount)
			{
				foreach (var indexToUnmount in _occupiedSlots.EnumerateOnes())
				{
					ref var load = ref _contentFileLoads[indexToUnmount];
					if (load.state != State.WaitingForUnmount)
					{
						continue;
					}

					load.archiveHandle.Unmount();
					load.state = State.Unmounting;
					if (++_ongoingUnmountingCount == MaxOngoingUnmountingCount)
					{
						break;
					}
				}
			}
		}

		bool IsLoaded(in ContentFileLoad load)
		{
			return load.state is State.Loaded or State.ErrorArchive or State.ErrorContentFiles;
		}

		bool IsSuccessfullyLoaded(in ContentFileLoad load)
		{
			return load.state is State.Loaded;
		}

		public struct ContentFileLoad
		{
			public State state;
			public SerializableGuid contentFileGuid;

			public ContentFile contentFile;
			public ArchiveHandle archiveHandle;
			public ContentFileUnloadHandle unloadHandle;

			public int referenceCount;

			public void Deconstruct(out ContentFile contentFile, out ArchiveHandle archiveHandle)
			{
				contentFile = this.contentFile;
				archiveHandle = this.archiveHandle;
			}
		}

		public enum State : byte
		{
			None,

			WaitingForMounting,
			Mounting,
			WaitingForDependencies,
			Loading,

			Loaded,
			ErrorArchive,
			ErrorContentFiles,

			WaitingToStartUnloading,
			Unloading,
			WaitingForUnmount,
			Unmounting,
		}
	}
}
