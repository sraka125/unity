using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Nokobot/Modern Guns/Simple Shoot")]
public class SimpleShoot : MonoBehaviour
{
    [Header("Prefab Refrences")]
    public GameObject bulletPrefab;
    public GameObject casingPrefab;
    public GameObject muzzleFlashPrefab;
    [SerializeField]
    AudioClip shoot;
    [SerializeField]
    AudioClip reload;

    [Header("Location Refrences")]
    [SerializeField] private Animator gunAnimator;
    [SerializeField] private Transform barrelLocation;
    [SerializeField] private Transform casingExitLocation;
    [SerializeField] private Transform magazine;

    [Header("Settings")]
    [Tooltip("Specify time to destory the casing object")] [SerializeField] private float destroyTimer = 2f;
    [Tooltip("Bullet Speed")] [SerializeField] private float shotPower = 500f;
    [Tooltip("Casing Ejection Speed")] [SerializeField] private float ejectPower = 150f;

    [Header("Reload")]
    [Tooltip("Rounds the magazine holds")] [SerializeField] private int magazineCapacity = 7;
    [Tooltip("Spare rounds carried outside the magazine")] [SerializeField] private int startingReserveAmmo = 14;
    [Tooltip("How far the magazine drops out of the gun, in local units")] [SerializeField] private float magazineDropDistance = 0.15f;
    [Tooltip("Seconds spent dropping the magazine")] [SerializeField] private float magazineDropTime = 0.2f;
    [Tooltip("Seconds spent swapping the magazine")] [SerializeField] private float magazineSwapTime = 0.3f;
    [Tooltip("Seconds spent pushing the magazine back in")] [SerializeField] private float magazineInsertTime = 0.2f;

    private Vector3 magazineHome;
    private bool isReloading;
    private int currentAmmo;
    private int reserveAmmo;

    public int CurrentAmmo => currentAmmo;
    public int MagazineCapacity => magazineCapacity;
    public int ReserveAmmo => reserveAmmo;
    public bool IsReloading => isReloading;

    public event System.Action OnAmmoChanged;

    void Start()
    {
        if (barrelLocation == null)
            barrelLocation = transform;

        if (gunAnimator == null)
            gunAnimator = GetComponentInChildren<Animator>();

        if (magazine != null)
            magazineHome = magazine.localPosition;

        currentAmmo = magazineCapacity;
        reserveAmmo = Mathf.Max(0, startingReserveAmmo);
        NotifyAmmoChanged();

        if (FindFirstObjectByType<AmmoHUD>() == null)
            new GameObject("AmmoHUD").AddComponent<AmmoHUD>();
    }

    void Update()
    {
        //If you want a different input, change it here
        if (Input.GetButtonDown("Fire1") && currentAmmo > 0 && !isReloading)
        {
            //Calls animation on the gun that has the relevant animation events that will fire
            gunAnimator.SetTrigger("Fire");
        }

        if (Input.GetButtonDown("Reload") && currentAmmo < magazineCapacity && reserveAmmo > 0)
        {
            Reload();
        }
    }

    //Call this from a UI button, or from another script
    public void Reload()
    {
        if (!magazine || isReloading || reserveAmmo <= 0 || currentAmmo >= magazineCapacity) { return; }

        isReloading = true;
        StartCoroutine(ReloadRoutine());
    }

    public void AddAmmo(int amount)
    {
        if (amount <= 0) { return; }

        reserveAmmo += amount;
        NotifyAmmoChanged();
    }

    public void ResetAmmo()
    {
        currentAmmo = magazineCapacity;
        reserveAmmo = Mathf.Max(0, startingReserveAmmo);
        NotifyAmmoChanged();
    }

    void NotifyAmmoChanged()
    {
        OnAmmoChanged?.Invoke();
    }

    IEnumerator ReloadRoutine()
    {
        //Plays the reload sound
        if (reload)
            PlayClip(reload);

        //Drop the magazine out
        yield return MoveMagazine(magazineHome - magazine.up * magazineDropDistance, magazineDropTime);

        //Swap the magazine while spinning the pistol a full turn
        yield return RotateGun360(magazineSwapTime);

        //Push it back in
        yield return MoveMagazine(magazineHome, magazineInsertTime);

        int needed = magazineCapacity - currentAmmo;
        int taken = Mathf.Min(needed, reserveAmmo);
        reserveAmmo -= taken;
        currentAmmo += taken;
        NotifyAmmoChanged();

        isReloading = false;
    }

    IEnumerator RotateGun360(float duration)
    {
        Quaternion start = transform.localRotation;
        float time = 0f;

        if (duration <= 0f)
        {
            transform.localRotation = start * Quaternion.Euler(360f, 0f, 0f);
            transform.localRotation = start;
            yield break;
        }

        while (time < duration)
        {
            time += Time.deltaTime;
            float angle = Mathf.Lerp(0f, 360f, Mathf.Clamp01(time / duration));
            transform.localRotation = start * Quaternion.Euler(angle, 0f, 0f);
            yield return null;
        }

        transform.localRotation = start;
    }

    IEnumerator MoveMagazine(Vector3 target, float duration)
    {
        if (duration <= 0f)
        {
            magazine.localPosition = target;
            yield break;
        }

        Vector3 start = magazine.localPosition;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            magazine.localPosition = Vector3.Lerp(start, target, Mathf.Clamp01(time / duration));
            yield return null;
        }

        magazine.localPosition = target;
    }

    void PlayClip(AudioClip clip)
    {
        if (!clip) { return; }

        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.Play();
        Destroy(src, clip.length);
    }


    //This function creates the bullet behavior
    void Shoot()
    {
        if (currentAmmo <= 0)
            return;

        currentAmmo--;
        NotifyAmmoChanged();

        if (muzzleFlashPrefab)
        {
            //Create the muzzle flash
            GameObject tempFlash;
            tempFlash = Instantiate(muzzleFlashPrefab, barrelLocation.position, barrelLocation.rotation);

            //Destroy the muzzle flash effect
            Destroy(tempFlash, destroyTimer);
        }

        //cancels if there's no bullet prefeb
        if (!bulletPrefab)
        { return; }

        // Create a bullet and add force on it in direction of the barrel
        Instantiate(bulletPrefab, barrelLocation.position, barrelLocation.rotation).GetComponent<Rigidbody>().AddForce(barrelLocation.forward * shotPower);
        if (shoot)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.clip = shoot;
            src.Play();
        }

        StartCoroutine(RecoilKick());

        IEnumerator RecoilKick()
        {
            Quaternion start = transform.localRotation;
            Quaternion kicked = start * Quaternion.Euler(-3f, 0f, 0f);
            const float kickTime = 0.05f;
            const float returnTime = 0.12f;

            float t = 0f;
            while (t < kickTime)
            {
                t += Time.deltaTime;
                transform.localRotation = Quaternion.Slerp(start, kicked, Mathf.Clamp01(t / kickTime));
                yield return null;
            }

            t = 0f;
            while (t < returnTime)
            {
                t += Time.deltaTime;
                transform.localRotation = Quaternion.Slerp(kicked, start, Mathf.Clamp01(t / returnTime));
                yield return null;
            }

            transform.localRotation = start;
        }
    }

    //This function creates a casing at the ejection slot
    void CasingRelease()
    {
        //Cancels function if ejection slot hasn't been set or there's no casing
        if (!casingExitLocation || !casingPrefab)
        { return; }

        //Create the casing
        GameObject tempCasing;
        tempCasing = Instantiate(casingPrefab, casingExitLocation.position, casingExitLocation.rotation) as GameObject;
        //Add force on casing to push it out
        tempCasing.GetComponent<Rigidbody>().AddExplosionForce(Random.Range(ejectPower * 0.7f, ejectPower), (casingExitLocation.position - casingExitLocation.right * 0.3f - casingExitLocation.up * 0.6f), 1f);
        //Add torque to make casing spin in random direction
        tempCasing.GetComponent<Rigidbody>().AddTorque(new Vector3(0, Random.Range(100f, 500f), Random.Range(100f, 1000f)), ForceMode.Impulse);

        //Destroy casing after X seconds
        Destroy(tempCasing, destroyTimer);
    }

}
