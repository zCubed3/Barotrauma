using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public interface IDamageable
    {
        Vector2 SimPosition
        {
            get;
        }

        Vector2 WorldPosition
        {
            get;
        }

        float Health
        {
            get;
        }
        
        AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound=true);
    }
}
