using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(fileName = "Unit Data", menuName = "Settings/UnitData", order = 1)]
public class UnitData : ScriptableObject
{
    
    
    public float BaseMorale;
    public float Health;

    public int MeleeAttackSkill; // Chance to attack unit
    public int MeleeDamage;

    public int MeleeDefenseSkill; // Chance to avoid attack
    public int Armor;

    public int Speed;
    public float RotationSpeed;


    [Header("Hidden Properties")] 
    public AABB aabb;
    // Size depends on cellsize used (probably 1 meter)
    public int Size;
    
    
    // TODO: bonuses (shielded, bonus vs cavalry, etc.)
    
    
}