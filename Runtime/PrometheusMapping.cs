using System.Collections.Generic;
using KVD.Utils.DataStructures;
using KVD.Utils.IO;
using Unity.Collections;
using UnityEngine;

namespace KVD.Prometheus
{
	public sealed class PrometheusMapping
	{
		public Dictionary<PrometheusIdentifier, SerializableGuid> Asset2ContentFile{ get; private set; }
		public Dictionary<PrometheusIdentifier, ulong> Asset2LocalIdentifier{ get; private set; }
		public Dictionary<SerializableGuid, SerializableGuid[]> ContentFile2Dependencies{ get; private set; }
		public Dictionary<SerializableGuid, SerializableGuid[]> ContentFile2Dependants{ get; private set; }

		public static PrometheusMapping Fresh()
		{
			return new()
			{
				Asset2ContentFile = new(),
				Asset2LocalIdentifier = new(),
				ContentFile2Dependencies = new(),
				ContentFile2Dependants = new(),
			};
		}

		public void Serialize(string filePath)
		{
			using var fileWriter = new FileWriter(filePath);

			fileWriter.Write(Asset2ContentFile.Count);
			foreach (var pair in Asset2ContentFile)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value);
			}

			fileWriter.Write(Asset2LocalIdentifier.Count);
			foreach (var pair in Asset2LocalIdentifier)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value);
			}

			fileWriter.Write(ContentFile2Dependencies.Count);
			foreach (var pair in ContentFile2Dependencies)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value.Length);
				foreach (var dependency in pair.Value)
				{
					fileWriter.Write(dependency);
				}
			}

			fileWriter.Write(ContentFile2Dependants.Count);
			foreach (var pair in ContentFile2Dependants)
			{
				fileWriter.Write(pair.Key);
				fileWriter.Write(pair.Value.Length);
				foreach (var dependant in pair.Value)
				{
					fileWriter.Write(dependant);
				}
			}
		}

		public void Deserialize(string filePath)
		{
			var fileContent = UnityFileRead.ToNewBuffer<byte>(filePath, Allocator.Temp);
			var reader = new BufferStreamReader(fileContent);
			var asset2ContentFileCount = reader.Read<int>();

			Asset2ContentFile = new(Mathf.CeilToInt(asset2ContentFileCount*1.2f));
			for (var i = 0; i < asset2ContentFileCount; i++)
			{
				var assetIdentifier = reader.Read<PrometheusIdentifier>();
				var contentFileGuid = reader.Read<SerializableGuid>();
				Asset2ContentFile[assetIdentifier] = contentFileGuid;
			}

			var asset2LocalIdentifierCount = reader.Read<int>();
			Asset2LocalIdentifier = new(Mathf.CeilToInt(asset2LocalIdentifierCount*1.2f));
			for (var i = 0; i < asset2LocalIdentifierCount; i++)
			{
				var assetIdentifier = reader.Read<PrometheusIdentifier>();
				var localIdentifier = reader.Read<ulong>();
				Asset2LocalIdentifier[assetIdentifier] = localIdentifier;
			}

			var contentFile2DependenciesCount = reader.Read<int>();
			ContentFile2Dependencies = new(Mathf.CeilToInt(contentFile2DependenciesCount*1.2f));
			for (var i = 0; i < contentFile2DependenciesCount; i++)
			{
				var contentFileGuid = reader.Read<SerializableGuid>();
				var dependenciesCount = reader.Read<int>();
				var dependencies = new SerializableGuid[dependenciesCount];
				for (var j = 0; j < dependenciesCount; j++)
				{
					dependencies[j] = reader.Read<SerializableGuid>();
				}
				ContentFile2Dependencies[contentFileGuid] = dependencies;
			}

			var contentFile2DependantsCount = reader.Read<int>();
			ContentFile2Dependants = new(Mathf.CeilToInt(contentFile2DependantsCount*1.2f));
			for (var i = 0; i < contentFile2DependantsCount; i++)
			{
				var contentFileGuid = reader.Read<SerializableGuid>();
				var dependantsCount = reader.Read<int>();
				var dependants = new SerializableGuid[dependantsCount];
				for (var j = 0; j < dependantsCount; j++)
				{
					dependants[j] = reader.Read<SerializableGuid>();
				}
				ContentFile2Dependants[contentFileGuid] = dependants;
			}

			fileContent.Dispose();
		}
	}
}
