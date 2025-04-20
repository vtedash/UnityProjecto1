using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "MyGame/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public string weaponName = "Default Weapon";
    public Sprite weaponSprite; // Imagen del arma
    public float attackDamageMultiplier = 1.0f; // 1.0 = sin cambio
    public float attackRangeMultiplier = 1.0f;
    public float attackCooldownMultiplier = 1.0f;
    // Más adelante añadiremos proyectiles, efectos, etc.
}