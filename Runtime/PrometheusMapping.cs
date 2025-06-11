using KVD.Utils.DataStructures;
using KVD.Utils.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace KVD.Prometheus
{
	public sealed class PrometheusMapping
	{
		public UnsafeHashMap<PrometheusIdentifier, SerializableGuid> asset2ContentFile;
		public UnsafeHashMap<PrometheusIdentifier, ulong> asset2LocalIdentifier;
		public UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>> contentFile2Dependencies;
		public UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>> contentFile2Dependants;

		public static PrometheusMapping Fresh(Allocator allocator)
		{
			return new()
			{
				asset2ContentFile = new(128, allocator),
				asset2LocalIdentifier = new(128, allocator),
				contentFile2Dependencies = new(128, allocator),
				contentFile2Dependants = new(128, allocator),
			};
		}

		public static PrometheusMapping Deserialize(string filePath, Allocator allocator)
		{
			var mapping = new PrometheusMapping();

			var fileContent = UnityFileRead.ToNewBuffer<byte>(filePath, Allocator.Temp);
			var reader = new BufferStreamReader(fileContent);

			var asset2ContentFileCount = reader.Read<int>();
			var asset2ContentFile = new UnsafeHashMap<PrometheusIdentifier, SerializableGuid>(Mathf.CeilToInt(asset2ContentFileCount*1.2f), allocator);
			for (var i = 0; i < asset2ContentFileCount; i++)
			{
				var assetIdentifier = reader.Read<PrometheusIdentifier>();
				var contentFileGuid = reader.Read<SerializableGuid>();
				asset2ContentFile[assetIdentifier] = contentFileGuid;
			}
			mapping.asset2ContentFile = asset2ContentFile;

			var asset2LocalIdentifierCount = reader.Read<int>();
			var asset2LocalIdentifier = new UnsafeHashMap<PrometheusIdentifier, ulong>(Mathf.CeilToInt(asset2LocalIdentifierCount*1.2f), allocator);
			for (var i = 0; i < asset2LocalIdentifierCount; i++)
			{
				var assetIdentifier = reader.Read<PrometheusIdentifier>();
				var localIdentifier = reader.Read<ulong>();
				asset2LocalIdentifier[assetIdentifier] = localIdentifier;
			}
			mapping.asset2LocalIdentifier = asset2LocalIdentifier;

			var contentFile2DependenciesCount = reader.Read<int>();
			var contentFile2Dependencies = new UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>>(Mathf.CeilToInt(contentFile2DependenciesCount*1.2f), allocator);
			for (var i = 0; i < contentFile2DependenciesCount; i++)
			{
				var contentFileGuid = reader.Read<SerializableGuid>();
				var dependenciesCount = reader.Read<uint>();
				var dependencies = new UnsafeArray<SerializableGuid>(dependenciesCount, allocator);
				for (var j = 0; j < dependenciesCount; j++)
				{
					dependencies[j] = reader.Read<SerializableGuid>();
				}
				contentFile2Dependencies[contentFileGuid] = dependencies;
			}
			mapping.contentFile2Dependencies = contentFile2Dependencies;

			var contentFile2DependantsCount = reader.Read<int>();
			var contentFile2Dependants = new UnsafeHashMap<SerializableGuid, UnsafeArray<SerializableGuid>>(Mathf.CeilToInt(contentFile2DependantsCount*1.2f), allocator);
			for (var i = 0; i < contentFile2DependantsCount; i++)
			{
				var contentFileGuid = reader.Read<SerializableGuid>();
				var dependantsCount = reader.Read<uint>();
				var dependants = new UnsafeArray<SerializableGuid>(dependantsCount, allocator);
				for (var j = 0; j < dependantsCount; j++)
				{
					dependants[j] = reader.Read<SerializableGuid>();
				}
				contentFile2Dependants[contentFileGuid] = dependants;
			}
			mapping.contentFile2Dependants = contentFile2Dependants;

			fileContent.Dispose();

			return mapping;
		}

		public void Dispose()
		{
			asset2ContentFile.Dispose();
			asset2LocalIdentifier.Dispose();

			var contentFile2DependenciesValues = contentFile2Dependencies.GetValueArray(Allocator.Temp);
			foreach (var dependencies in contentFile2DependenciesValues)
			{
				dependencies.Dispose();
			}
			contentFile2DependenciesValues.Dispose();
			contentFile2Dependencies.Dispose();

			var contentFile2DependantsValues = contentFile2Dependants.GetValueArray(Allocator.Temp);
			foreach (var dependants in contentFile2DependantsValues)
			{
				dependants.Dispose();
			}
			contentFile2DependantsValues.Dispose();
			contentFile2Dependants.Dispose();
		}

		public void Serialize(string filePath)
		{
			using var fileWriter = new FileWriter(filePath);

			fileWriter.Write(asset2ContentFile.Count);
			foreach (var pair in asset2ContentFile)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value);
			}

			fileWriter.Write(asset2LocalIdentifier.Count);
			foreach (var pair in asset2LocalIdentifier)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value);
			}

			fileWriter.Write(contentFile2Dependencies.Count);
			foreach (var pair in contentFile2Dependencies)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value.Length);
				foreach (var dependency in pair.Value)
				{
					fileWriter.Write(dependency);
				}
			}

			fileWriter.Write(contentFile2Dependants.Count);
			foreach (var pair in contentFile2Dependants)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value.Length);
				foreach (var dependant in pair.Value)
				{
					fileWriter.Write(dependant);
				}
			}
		}
	}
}
