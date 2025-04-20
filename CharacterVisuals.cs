using UnityEngine;

// Este script se añade al prefab del Luchador
[RequireComponent(typeof(CharacterData))]
public class CharacterVisuals : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Asigna aquí el SpriteRenderer hijo que mostrará el arma")]
    public SpriteRenderer weaponSpriteRenderer; // Arrastra el hijo aquí en el Inspector del Prefab

    private CharacterData characterData;

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
        if (weaponSpriteRenderer == null) {
            Debug.LogWarning($"[{gameObject.name}] Weapon Sprite Renderer not assigned in CharacterVisuals.", this);
        }
    }

    // Llamado por BattleManager después de aplicar datos
    public void UpdateWeaponVisuals()
    {
        if (characterData != null && weaponSpriteRenderer != null)
        {
             // Asigna el sprite del arma equipada (si existe)
            weaponSpriteRenderer.sprite = characterData.equippedWeapon?.weaponSprite;
        }
         else if (weaponSpriteRenderer != null)
        {
             // Si no hay arma o renderer, limpia el sprite
             weaponSpriteRenderer.sprite = null;
        }
    }

    // Podrías añadir aquí lógica para cambiar colores, ropa, etc. basado en otros datos guardados en el futuro
}