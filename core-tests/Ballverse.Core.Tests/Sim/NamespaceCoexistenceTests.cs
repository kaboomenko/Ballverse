using Ballverse.Core.Params;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Mode code uses Ballverse.Core.Params (body budgets) and Ballverse.Core.Sim (physics) together; this file compiles only
    /// while the two namespaces share no public type names used by modes (Params.BodyKind vs Sim.BodyMotion).
    /// </summary>
    public class NamespaceCoexistenceTests
    {
        [Fact]
        public void ParamsAndSimNamespacesCanBeImportedTogether()
        {
            var def = new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f };
            BodyKind budgetKind = BodyKind.Balls;
            var w = new PhysicsWorld();
            int body = w.AddBody(def);
            Assert.Equal(BodyMotion.Kinematic, w.GetBody(body).Motion);
            Assert.Equal(BodyKind.Balls, budgetKind);
        }
    }
}
