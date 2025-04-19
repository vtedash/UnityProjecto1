using UnityEngine;
using System.Collections.Generic; // Necesario para listas si añades efectos complejos

// --- ENUMERACIONES (Puedes ponerlas dentro o fuera de la clase) ---
public enum SkillType { DirectDamage, Projectile, SelfBuff, AreaOfEffect, Debuff, Heal }
public enum StatToBuff { Speed, Damage, Defense, AttackSpeed } // Añade más según sea necesario

[CreateAssetMenu(fileName = "NewSkill", menuName = "MyGame/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("Identification")]
    public string skillID = System.Guid.NewGuid().ToString(); // ID único autogenerado
    public string skillName = "New Skill";
    [TextArea] public string description = "Skill description.";
    public Sprite icon; // Para UI

    [Header("Core Mechanics")]
    public SkillType skillType = SkillType.DirectDamage;
    public float cooldown = 5.0f;
    [Tooltip("Range to activate/apply the skill. 0 means self.")]
    public float range = 1.0f;
    // public float manaCost = 10f; // Descomentar si implementas Mana

    [Header("Effect Values")]
    [Tooltip("Damage dealt by the skill (if applicable)")]
    public float damage = 20f;
    [Tooltip("Amount healed by the skill (if applicable)")]
    public float healAmount = 30f;

    [Header("Buff/Debuff Values (if applicable)")]
    public float duration = 5.0f;
    public StatToBuff buffStat = StatToBuff.Speed; // Qué stat afecta
    [Tooltip("Multiplier for the buff/debuff. E.g., 1.5 = +50%, 0.8 = -20%")]
    public float buffMultiplier = 1.5f;
    // public StatusEffectData statusEffectPrefab; // Para aplicar DoT, Slow, etc. (Más avanzado)

    [Header("Projectile (if applicable)")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;
    public float projectileLifetime = 5f; // Tiempo antes de que el proyectil se destruya solo

    [Header("Area of Effect (if applicable)")]
    public float aoeRadius = 3f;
    public bool affectsSelf = false; // El AoE afecta al lanzador?
    public bool affectsAllies = false; // El AoE afecta aliados?
    public bool affectsEnemies = true; // El AoE afecta enemigos?

    [Header("Visuals & Audio")]
    public GameObject castVFX; // Efecto al lanzar
    public GameObject hitVFX; // Efecto al impactar (si aplica)
    public AudioClip castSFX;
    public AudioClip hitSFX;
    [Tooltip("Name of the trigger parameter in the Animator")]
    public string animationTriggerName = "UseSkill"; // Nombre genérico o específico
}