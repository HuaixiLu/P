﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnitTests.Core;
using UnitTests.Runners;
using UnitTests.Validators;

namespace UnitTests
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class PSharpBackendTests
    {
        [Test]
        public void TestCompilePSharpTemp()
        {
            DirectoryInfo tempDir = Directory.CreateDirectory(Path.Combine(Constants.ScratchParentDirectory, "TestPSharpTemp"));
            var tempFilePath = new FileInfo(Path.Combine(Constants.SolutionDirectory, "tmp", "test.p"));

            if (!tempFilePath.Exists)
            {
                return;
            }

            var testCase = new CompilerTestCase(
                tempDir,
                new PSharpRunner(new[] {tempFilePath}),
                new CompileSuccessValidator());

            TestAssertions.AssertTestCase(testCase);
        }
    }
}