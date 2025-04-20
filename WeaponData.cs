using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "MyGame/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public string weaponName = "Default Weapon";
    public Sprite weaponSprite; // Imagen del arma (Asigna sprites en Unity si los tienes)
    [Tooltip("Ej: 1.0 = Daño base, 1.5 = 50% más daño, 0.8 = 20% menos daño")]
    public float attackDamageMultiplier = 1.0f;
    [Tooltip("Ej: 1.0 = Rango base, 1.5 = 50% más rango")]
    public float attackRangeMultiplier = 1.0f;
    [Tooltip("Ej: 1.0 = Cooldown base, 1.2 = 20% más lento, 0.7 = 30% más rápido")]
    public float attackCooldownMultiplier = 1.0f;
    // Más adelante: public GameObject projectilePrefab;
    // Más adelante: public string animationTriggerOverride;
}