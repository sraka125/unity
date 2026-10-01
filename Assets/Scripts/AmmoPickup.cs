using UnityEngine;
using SUPERCharacter;

[RequireComponent(typeof(Collider))]
public class AmmoPickup : MonoBehaviour, ICollectable
{
    [SerializeField] private int ammoAmount = 7;
    [SerializeField] private bool destroyOnCollect = true;
    [SerializeField] private float spinSpeed = 45f;
    [SerializeField] private float bobHeight = 0.08f;

    private Vector3 startPos;
    private bool collected;

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col)
            col.isTrigger = true;
    }

    void Start()
    {
        startPos = transform.position;

        Collider col = GetComponent<Collider>();
        if (col)
            col.isTrigger = true;
    }

    void Update()
    {
        if (collected) { return; }

        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        transform.position = startPos + Vector3.up * (Mathf.Sin(Time.time * Mathf.PI) * bobHeight);
    }

    public void Collect()
    {
        if (collected) { return; }

        SimpleShoot gun = FindFirstObjectByType<SimpleShoot>();
        if (!gun) { return; }

        collected = true;
        gun.AddAmmo(ammoAmount);

        if (destroyOnCollect)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (collected) { return; }

        if (other.GetComponentInParent<SUPERCharacterAIO>() || other.CompareTag("Player"))
            Collect();
    }
}
