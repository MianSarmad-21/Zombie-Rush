namespace ZombieRush.Core
{
    /// Anything that can take hitscan/projectile damage (zombies, breakables, etc.
    /// to be added later).
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }
}
