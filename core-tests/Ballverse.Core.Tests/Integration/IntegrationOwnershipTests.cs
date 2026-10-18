using System;
using System.Linq;
using Ballverse.Core.Integration;
using Xunit;

namespace Ballverse.Core.Tests.Integration
{
    /// <summary>
    /// Static checks that the integration layer adds glue only, never a second copy of a module concept (QA core-integration
    /// round 1: a third <c>WatermarkPolicy</c> made <c>using Ballverse.Core.Integration; using Ballverse.Core.Projects;</c> fail
    /// with CS0104 after merging with main).
    /// </summary>
    public class IntegrationOwnershipTests
    {
        static readonly Type[] s_coreTypes = typeof(RemoteConfigAdapters).Assembly.GetExportedTypes();

        /// <summary>
        /// Types owned by modules on other branches (Projects, Monetization) that the Core assembly of this branch cannot see;
        /// Integration must not define them either.
        /// </summary>
        static readonly string[] s_ownedElsewhere = { "WatermarkPolicy", "WatermarkRow", "AdPolicyConfigMapper", "MonetizationConfig" };

        [Fact]
        public void IntegrationTypeNamesAreUniqueAcrossCore()
        {
            var integration = s_coreTypes.Where(t => t.Namespace == "Ballverse.Core.Integration").ToArray();
            Assert.NotEmpty(integration);
            foreach (Type t in integration)
            {
                Type clash = s_coreTypes.FirstOrDefault(o => o != t && o.Name == t.Name && o.Namespace != t.Namespace);
                Assert.True(clash == null, t.Name + " also exists as " + clash?.FullName);
                Assert.DoesNotContain(t.Name, s_ownedElsewhere);
            }
        }
    }
}
