using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;


// TODO Do not use IComponentData (we're using it b/c it's easily to with Entities.Foreach with it)
public struct UnitDataBlobReference : IComponentData 
{
    private BlobAssetReference<AABBBlobAsset> AABBBlobAssetRef;

    public UnitDataBlobReference(BlobAssetReference<AABBBlobAsset> AABBBlobAssetRef)
    {
        this.AABBBlobAssetRef = AABBBlobAssetRef;
    }

    public AABB Value => AABBBlobAssetRef.Value.Ptr.Value;
}

public struct AABBBlobAsset
{
    public BlobPtr<AABB> Ptr;
}