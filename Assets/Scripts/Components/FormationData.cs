using System;
using Unity.Entities;

[Serializable]
public struct FormationGroup : ISharedComponentData {
    public int ID;
}

[Serializable]
public struct FormationIndex : IComponentData {
    public int Index; // The unit's index in their formation
}

[Flags]
public enum FormationCombatBehavior : byte
{
    OVERRUN    = 1 << 0,
    HOLDING    = 1 << 1,
    RETREATING = 1 << 2, 
    BOUNCING   = 1 << 3
}

[Serializable]
public struct FormationBehavior : ISharedComponentData
{
    public FormationCombatBehavior Value;
}




[Serializable]
public struct TestudoFormationTag : IComponentData {}

[Serializable]
public struct OrbFormationTag : IComponentData {}

[Serializable]
public struct WedgeFormationTag : IComponentData {}