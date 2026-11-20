using System;
using RunningOrders.Services;
using LogicalEnd = Model.Car.LogicalEnd;

namespace RunningOrders.Tests
{
    // CutSelection on plain test cars (the game's cars can't be made outside the game). Each case lists the cars in the
    // order UncouplingService hands them over.
    public class CutSelectionTest
    {
        // A test car: id (its road number), the area its waybill sends it to (null: none), and whether it's a
        // locomotive.
        private sealed class TestCar(int number, string? area = null, bool isLoco = false)
        {
            public string Id { get; } = number.ToString();
            public string? Area { get; } = area;
            public bool IsLoco { get; } = isLoco;
        }

        private static string Id(TestCar car) => car.Id;

        private static Func<TestCar, bool> BoundFor(string area) => car => car.Area == area;

        private static void AssertCut(List<TestCar> result, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            Assert.Equal(expectedCount, result.Count);
            if (expectedCount > 0)
            {
                Assert.Equal(expectedFirstId, result.First().Id);
                Assert.Equal(expectedLastId, result.Last().Id);
            }
        }

        // ---------- Where to uncouple ----------

        // 0-1-2-3-4-5-6-7-8-9
        private static List<TestCar> PlainConsist() => [.. Enumerable.Range(0, 10).Select(n => new TestCar(n))];

        private static List<TestCar> Cars(params int[] numbers) => [.. numbers.Select(n => new TestCar(n))];

        [Fact]
        public void UncouplePoint_FromEndA()
        {
            List<TestCar> consist = PlainConsist();
            var (index, end) = CutSelection.UncouplePoint(Cars(0, 1, 2), consist, Id);
            Assert.Equal("2", consist[index].Id);
            Assert.Equal(LogicalEnd.B, end);
        }

        [Fact]
        public void UncouplePoint_FromEndB()
        {
            List<TestCar> consist = PlainConsist();
            var (index, end) = CutSelection.UncouplePoint(Cars(7, 8, 9), consist, Id);
            Assert.Equal("7", consist[index].Id);
            Assert.Equal(LogicalEnd.A, end);
        }

        [Fact]
        public void UncouplePoint_FromEndB_OrderDoesNotMatter()
        {
            List<TestCar> consist = PlainConsist();
            var (index, end) = CutSelection.UncouplePoint(Cars(9, 8, 7, 6), consist, Id);
            Assert.Equal("6", consist[index].Id);
            Assert.Equal(LogicalEnd.A, end);
        }

        [Fact]
        public void UncouplePoint_Throws_WhenCarsAreNotInConsist()
        {
            Assert.Throws<InvalidOperationException>(() => CutSelection.UncouplePoint(Cars(15, 16), PlainConsist(), Id));
        }

        [Fact]
        public void UncouplePoint_Throws_WhenCutIsInTheMiddle()
        {
            Assert.Throws<InvalidOperationException>(() => CutSelection.UncouplePoint(Cars(3, 4), PlainConsist(), Id));
        }

        // ---------- By count ----------

        // S - Sylva, W - Whittier, B - Bryson, L - Loco
        // S-S-W-L-B-B-S-W-W-S-B
        // 0-1-2-3-4-5-6-7-8-9-10
        private static List<TestCar> DestinationConsist() =>
        [
            new(0, "Sylva"), new(1, "Sylva"), new(2, "Whittier"), new(3, isLoco: true), new(4, "Bryson"), new(5, "Bryson"),
            new(6, "Sylva"), new(7, "Whittier"), new(8, "Whittier"), new(9, "Sylva"), new(10, "Bryson"),
        ];

        // <--- direction of couple means pickups always use far side
        // FAR  0-1-2-3-4-5-6-7-8-9-10  NEAR
        [Theory]
        [InlineData(4, 2, 3, "0", "2")]
        [InlineData(4, 150, 0, null, null)]
        [InlineData(9, 1, 9, "0", "8")]
        [InlineData(0, 1, 0, null, null)]
        [InlineData(1, 1, 1, "0", "0")]
        [InlineData(9, 5, 5, "0", "4")]
        public void PickupByCount(int indexOfCoupled, int carsToPickup, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            AssertCut(CutSelection.PickupByCount(DestinationConsist(), indexOfCoupled, carsToPickup), expectedCount, expectedFirstId, expectedLastId);
        }

        // <--- direction of couple means dropoffs always use far side
        // FAR  0-1-2-3-4-5-6-7-8-9-10  NEAR
        [Theory]
        [InlineData(4, 2, 7, "0", "6")]
        [InlineData(4, 150, 11, "0", "10")]
        [InlineData(6, 1, 8, "0", "7")]
        [InlineData(0, 1, 2, "0", "1")]
        [InlineData(9, 0, 0, null, null)]
        [InlineData(5, 0, 0, null, null)]
        [InlineData(10, 0, 0, null, null)]
        [InlineData(3, 2, 6, "0", "5")]
        public void DropoffByCount(int indexOfCoupled, int carsToDropoff, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            AssertCut(CutSelection.DropoffByCount(DestinationConsist(), indexOfCoupled, carsToDropoff), expectedCount, expectedFirstId, expectedLastId);
        }

        // ---------- By destination ----------

        // The train read from the end nearest the waypoint (closestBlock) or the far end.
        [Theory]
        [InlineData("Whittier", true, true, 2, "0", "1")]
        [InlineData("Whittier", true, false, 2, "10", "9")]
        [InlineData("Whittier", false, true, 3, "0", "2")]
        [InlineData("Whittier", false, false, 4, "10", "7")]
        [InlineData("Sylva", true, true, 0, null, null)]
        [InlineData("Sylva", true, false, 1, "10", "10")]
        [InlineData("Sylva", false, true, 2, "0", "1")]
        [InlineData("Sylva", false, false, 2, "10", "9")]
        [InlineData("Bryson", true, true, 4, "0", "3")]
        [InlineData("Bryson", true, false, 0, null, null)]
        [InlineData("Bryson", false, true, 6, "0", "5")]
        [InlineData("Bryson", false, false, 1, "10", "10")]
        [InlineData("Dillsboro", true, true, 0, null, null)]
        [InlineData("Dillsboro", false, true, 0, null, null)]
        [InlineData("Dillsboro", false, false, 0, null, null)]
        public void CutByDestinationArea(string area, bool excludeMatchFromCut, bool closestBlock, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            List<TestCar> fromEnd = DestinationConsist();
            if (!closestBlock)
            {
                fromEnd.Reverse();
            }
            AssertCut(CutSelection.CutByMatch(fromEnd, BoundFor(area), excludeMatchFromCut), expectedCount, expectedFirstId, expectedLastId);
        }

        // S - Sylva, W - Whittier, B - Bryson, L - Loco
        // S-S-W-B-B-W-S-W-B-W-S-B-L
        // 0-1-2-3-4-5-6-7-8-9-10-11-12
        private static List<TestCar> PostCoupleConsist() =>
        [
            new(0, "Sylva"), new(1, "Sylva"), new(2, "Whittier"), new(3, "Bryson"), new(4, "Bryson"), new(5, "Whittier"),
            new(6, "Sylva"), new(7, "Whittier"), new(8, "Bryson"), new(9, "Whittier"), new(10, "Sylva"), new(11, "Bryson"),
            new(12, isLoco: true),
        ];

        [Theory]
        [InlineData(5, "Whittier", true, true, 6, "5", "0")]
        [InlineData(5, "Whittier", true, false, 3, "0", "2")]
        [InlineData(5, "Whittier", false, true, 5, "4", "0")]
        [InlineData(5, "Whittier", false, false, 2, "0", "1")]
        [InlineData(5, "Bryson", true, true, 5, "4", "0")]
        [InlineData(5, "Bryson", true, false, 5, "0", "4")]
        [InlineData(5, "Bryson", false, true, 3, "2", "0")]
        [InlineData(5, "Bryson", false, false, 3, "0", "2")]
        [InlineData(5, "Sylva", true, true, 2, "1", "0")]
        [InlineData(5, "Sylva", true, false, 2, "0", "1")]
        [InlineData(5, "Sylva", false, true, 0, null, null)]
        [InlineData(5, "Sylva", false, false, 0, null, null)]
        [InlineData(5, "Dillsboro", false, true, 6, "0", "5")]
        [InlineData(5, "Dillsboro", false, false, 6, "0", "5")]
        public void PickupByDestinationArea(int indexOfCoupled, string area, bool excludeMatchFromCut, bool closestBlock, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            List<TestCar> available = CutSelection.AvailableForPickup(PostCoupleConsist(), indexOfCoupled);
            AssertCut(CutSelection.PickupByMatch(available, BoundFor(area), excludeMatchFromCut, closestBlock, Id), expectedCount, expectedFirstId, expectedLastId);
        }

        [Theory]
        [InlineData(5, "Whittier", true, true, 7, "0", "6")]
        [InlineData(5, "Whittier", true, false, 9, "0", "8")]
        [InlineData(5, "Whittier", false, true, 8, "0", "7")]
        [InlineData(5, "Whittier", false, false, 10, "0", "9")]
        [InlineData(5, "Bryson", true, true, 8, "0", "7")]
        [InlineData(5, "Bryson", true, false, 11, "0", "10")]
        [InlineData(5, "Bryson", false, true, 9, "0", "8")]
        [InlineData(5, "Bryson", false, false, 12, "0", "11")]
        [InlineData(5, "Sylva", true, true, 0, null, null)]
        [InlineData(5, "Sylva", true, false, 10, "0", "9")]
        [InlineData(5, "Sylva", false, true, 7, "0", "6")]
        [InlineData(5, "Sylva", false, false, 11, "0", "10")]
        [InlineData(5, "Dillsboro", false, true, 0, null, null)]
        [InlineData(5, "Dillsboro", false, false, 0, null, null)]
        public void DropoffByDestinationArea(int indexOfCoupled, string area, bool excludeMatchFromCut, bool closestBlock, int expectedCount, string? expectedFirstId, string? expectedLastId)
        {
            List<TestCar> consist = PostCoupleConsist();
            List<TestCar> available = CutSelection.AvailableForDropoff(consist, indexOfCoupled);
            AssertCut(CutSelection.DropoffByMatch(available, consist, BoundFor(area), excludeMatchFromCut, closestBlock, Id), expectedCount, expectedFirstId, expectedLastId);
        }

        // ---------- All except locomotives ----------

        // L-S-W-B-B-W-S-L-B-W-S-B-L
        // 0-1-2-3-4-5-6-7-8-9-10-11-12
        [Theory]
        [InlineData(4, 1, "0", "0")]
        [InlineData(9, 8, "7", "0")]
        [InlineData(7, 8, "7", "0")]
        public void PickupAllExceptLocomotives(int indexOfCoupled, int expectedCount, string expectedFirstId, string expectedLastId)
        {
            List<TestCar> consist =
            [
                new(0, isLoco: true), new(1, "Sylva"), new(2, "Whittier"), new(3, "Bryson"), new(4, "Bryson"), new(5, "Whittier"),
                new(6, "Sylva"), new(7, isLoco: true), new(8, "Bryson"), new(9, "Whittier"), new(10, "Sylva"), new(11, "Bryson"),
                new(12, isLoco: true),
            ];
            List<TestCar> available = CutSelection.AvailableForPickup(consist, indexOfCoupled);
            AssertCut(CutSelection.PickupAllExceptLocomotives(available, car => car.IsLoco, Id), expectedCount, expectedFirstId, expectedLastId);
        }

        // S-S-W-B-B-W-S-L-B-W-S-B-L
        // 0-1-2-3-4-5-6-7-8-9-10-11-12
        [Theory]
        [InlineData(4, 7, "0", "6", true)]
        [InlineData(9, 12, "0", "11", true)]
        // Locomotive 7 hauled dead, in tow: it's dropped with the cars, up to the working locomotive 12
        [InlineData(4, 12, "0", "11", false)]
        public void DropoffAllExceptLocomotives(int indexOfCoupled, int expectedCount, string expectedFirstId, string expectedLastId, bool loco7Working)
        {
            List<TestCar> consist =
            [
                new(0, "Sylva"), new(1, "Sylva"), new(2, "Whittier"), new(3, "Bryson"), new(4, "Bryson"), new(5, "Whittier"),
                new(6, "Sylva"), new(7, isLoco: true), new(8, "Bryson"), new(9, "Whittier"), new(10, "Sylva"), new(11, "Bryson"),
                new(12, isLoco: true),
            ];
            HashSet<string> working = loco7Working ? ["7", "12"] : ["12"];
            List<TestCar> available = CutSelection.AvailableForDropoff(consist, indexOfCoupled);
            AssertCut(CutSelection.DropoffAllExceptLocomotives(available, consist, car => working.Contains(car.Id), Id), expectedCount, expectedFirstId, expectedLastId);
        }
    }
}
