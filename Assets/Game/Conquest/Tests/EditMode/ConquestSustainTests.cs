using NUnit.Framework;
using Game.Conquest.Classes;
using Game.Conquest.Combat;

namespace Game.Conquest.Tests
{
    public class ConquestSustainTests
    {
        // 10 shots to overheat, cooling waits 0.3 s, then 1 s from hot to cold; 2 s lockout.
        private static SustainParams Heat(float after = 0f) =>
            new SustainParams(ConquestWeaponModel.Overheat, 30, 1.5f, 10, 0.3f, 1f, 2f, after);

        private static SustainParams Mag() =>
            new SustainParams(ConquestWeaponModel.Magazine, 5, 1.5f, 10, 0.3f, 1f, 2f, 0f);

        [Test]
        public void SustainedFireOverheatsOnExactlyTheLastShot()
        {
            var p = Heat();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            double t = 0;
            for (int i = 0; i < 9; i++)
            {
                Assert.IsTrue(pred.CanFire(p, t), $"shot {i}");
                pred.OnShot(p, t);
                t += 0.1;
            }
            Assert.IsFalse(pred.IsOverheated);
            Assert.IsTrue(pred.CanFire(p, t));
            pred.OnShot(p, t);
            Assert.IsTrue(pred.IsOverheated);
            Assert.IsFalse(pred.CanFire(p, t + 1.9));
            Assert.IsTrue(pred.CanFire(p, t + 2.01));
            Assert.AreEqual(0f, pred.HeatShots(p, t + 2.01), 1e-4f);
        }

        [Test]
        public void NoCoolingBeforeTheDelayThenLinear()
        {
            var p = Heat();
            float heat = 0f;
            double coolFrom = 0;
            ConquestHeatModel.ApplyShot(ref heat, ref coolFrom, 0.0, p);
            ConquestHeatModel.ApplyShot(ref heat, ref coolFrom, 0.0, p);
            Assert.AreEqual(2f, ConquestHeatModel.HeatAt(heat, coolFrom, 0.29, p.CoolPerSecond), 1e-4f);
            // 10 shots per second of cooling: 0.1 s after the delay is one shot cooler.
            Assert.AreEqual(1f, ConquestHeatModel.HeatAt(heat, coolFrom, 0.4, p.CoolPerSecond), 1e-4f);
            Assert.AreEqual(0f, ConquestHeatModel.HeatAt(heat, coolFrom, 5.0, p.CoolPerSecond), 1e-4f);
        }

        [Test]
        public void LazyHeatMatchesStepByStepEvaluation()
        {
            var p = Heat();
            float heat = 0f;
            double coolFrom = 0;
            for (int i = 0; i < 6; i++) ConquestHeatModel.ApplyShot(ref heat, ref coolFrom, i * 0.2, p);
            float lazy = ConquestHeatModel.HeatAt(heat, coolFrom, 2.0, p.CoolPerSecond);
            // Re-evaluating at many intermediate times must not change the answer.
            for (double t = 1.0; t < 2.0; t += 0.01) ConquestHeatModel.HeatAt(heat, coolFrom, t, p.CoolPerSecond);
            Assert.AreEqual(lazy, ConquestHeatModel.HeatAt(heat, coolFrom, 2.0, p.CoolPerSecond), 1e-6f);
        }

        [Test]
        public void OverheatLockEndsAtHeatAfter()
        {
            var p = Heat(0.5f);
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            for (int i = 0; i < 10; i++) pred.OnShot(p, 0.0);
            Assert.IsTrue(pred.IsOverheated);
            Assert.AreEqual(1f, pred.Heat01(p, 0.0), 1e-4f);
            pred.Tick(p, 2.0);
            Assert.IsFalse(pred.IsOverheated);
            Assert.AreEqual(0.5f, pred.Heat01(p, 2.0), 1e-4f);
        }

        [Test]
        public void MagazineAmmoFollowsSnapshotMinusUnacknowledgedShots()
        {
            var p = Mag();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(0, 0f, 0, 5, WeaponLock.None, 0, 1), 0, 0f);
            pred.OnShot(p, 0.0);
            pred.OnShot(p, 0.1);
            pred.OnShot(p, 0.2);
            // The server has processed only the first shot.
            pred.OnSnapshot(p, WeaponOwnerState.Pack(1, 0f, 0, 4, WeaponLock.None, 0, 1), 0.25, 0.05f);
            Assert.AreEqual(2, pred.Ammo);
        }

        [Test]
        public void AckCountWrapsAround()
        {
            var p = Mag();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.ResyncSeq(65534);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(65534, 0f, 0, 5, WeaponLock.None, 0, 0), 0, 0f);
            pred.OnShot(p, 0.0);
            pred.OnShot(p, 0.1);
            Assert.AreEqual((ushort)0, pred.Sent);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(65535, 0f, 0, 4, WeaponLock.None, 0, 0), 0.2, 0f);
            Assert.AreEqual(3, pred.Ammo);
        }

        [Test]
        public void StaleReloadingSnapshotAfterLocalReloadIsIgnored()
        {
            var p = Mag();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(0, 0f, 0, 5, WeaponLock.None, 0, 0), 0, 0f);
            for (int i = 0; i < 5; i++) pred.OnShot(p, i * 0.1);
            Assert.IsTrue(pred.IsReloading);
            pred.Tick(p, 2.0);
            Assert.IsFalse(pred.IsReloading);
            Assert.AreEqual(5, pred.Ammo);
            // The server's lock-start snapshot for the same reload arrives late, with time still
            // left on its clock: it is the lock this peer already predicted, so it isn't re-applied.
            pred.OnSnapshot(p, WeaponOwnerState.Pack(5, 0f, 1.5, 0, WeaponLock.Reload, 1, 0), 2.0, 0.1f);
            Assert.IsFalse(pred.IsReloading);
            Assert.AreEqual(5, pred.Ammo);
        }

        [Test]
        public void EpochChangeHardAdopts()
        {
            var p = Heat();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(0, 0f, 0, 30, WeaponLock.None, 0, 3), 0, 0f);
            for (int i = 0; i < 6; i++) pred.OnShot(p, 0.0);
            // Respawn on the server: cold weapon, new epoch, all six shots acknowledged.
            pred.OnSnapshot(p, WeaponOwnerState.Pack(6, 0f, 0, 30, WeaponLock.None, 0, 4), 0.1, 0.05f);
            Assert.AreEqual(0f, pred.HeatShots(p, 0.1), 1e-4f);
        }

        [Test]
        public void ServerLockIsAdoptedWithLatencyTakenOff()
        {
            var p = Heat();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(0, 0f, 0, 30, WeaponLock.None, 0, 0), 0, 0f);
            for (int i = 0; i < 9; i++) pred.OnShot(p, 0.0);
            Assert.IsFalse(pred.IsOverheated);
            // The server overheated on the ninth shot (2 s lock, written 0.1 s ago).
            pred.OnSnapshot(p, WeaponOwnerState.Pack(9, 10f, 2.0, 30, WeaponLock.Overheat, 1, 0), 1.0, 0.1f);
            Assert.IsTrue(pred.IsOverheated);
            Assert.IsFalse(pred.CanFire(p, 2.85));
            Assert.IsTrue(pred.CanFire(p, 2.95));
        }

        [Test]
        public void PredictedLockIsNotReleasedByALenientServer()
        {
            var p = Heat();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnSnapshot(p, WeaponOwnerState.Pack(0, 0f, 0, 30, WeaponLock.None, 0, 0), 0, 0f);
            for (int i = 0; i < 10; i++) pred.OnShot(p, 0.0);
            Assert.IsTrue(pred.IsOverheated);
            // The server is a little cooler and not locked.
            pred.OnSnapshot(p, WeaponOwnerState.Pack(10, 9.4f, 0, 30, WeaponLock.None, 0, 0), 0.1, 0.05f);
            Assert.IsTrue(pred.IsOverheated);
        }

        [Test]
        public void ResyncContinuesFromTheServerCount()
        {
            var pred = new ConquestSustainPredictor();
            pred.ResyncSeq(42);
            Assert.AreEqual((ushort)42, pred.Sent);
        }

        [Test]
        public void HeatWeaponsNeverReload()
        {
            var p = Heat();
            var pred = new ConquestSustainPredictor();
            pred.Reset(p);
            pred.OnShot(p, 0.0);
            Assert.IsFalse(pred.OnReloadRequested(p, 0.1));
            Assert.IsFalse(pred.IsReloading);
        }

        [Test]
        public void OwnerStatePacksAndUnpacks()
        {
            var s = WeaponOwnerState.Pack(1234, 7.5f, 1.2345, 77, WeaponLock.Overheat, 2, 13);
            Assert.AreEqual((ushort)1234, s.ackCount);
            Assert.AreEqual(7.5f, s.Heat, 1e-3f);
            Assert.AreEqual((ushort)1235, s.lockMsLeft);
            Assert.AreEqual((byte)77, s.ammo);
            Assert.IsTrue(s.Overheated);
            Assert.IsFalse(s.Reloading);
            Assert.AreEqual((byte)13, s.Epoch);
            Assert.AreEqual((byte)2, s.LockCount);
        }
    }
}
