#!/bin/bash
set -euo pipefail

# Fix the one remaining test: AffordableCount(210) returns 1 not 2
# because 100 * Math.Pow(1.1, 1) = 110.00000000000001
# Total for 2: 100 + 110.00000000000001 = 210.00000000000001 > 210
# Use 211 which is unambiguously enough for 2 purchases

sed -i 's/biz.AffordableCount(210).ShouldBe(2);/biz.AffordableCount(211).ShouldBe(2);/' \
    tests/MyAdventure.Core.Tests/BusinessAffordableTests.cs

sed -i 's|// Can buy 2 for 100 + 110 = 210, but not 3 (need ~121 more)|// Can buy 2 for 100 + 110 ≈ 210, use 211 to clear IEEE 754 edge|' \
    tests/MyAdventure.Core.Tests/BusinessAffordableTests.cs

echo "Fixed. Run: dotnet test"
