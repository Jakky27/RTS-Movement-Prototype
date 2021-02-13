using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/*public static class BlobAssetUtils {
    public static BlobAssetReference<T> CreateReference<T>(T value, Allocator allocator) where T : struct {
        BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref T data = ref builder.ConstructRoot<T>();
        data = value;
        BlobAssetReference<T> reference = builder.CreateBlobAssetReference<T>(allocator);
        builder.Dispose();
 
        return reference;
    }
}*/