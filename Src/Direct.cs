using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Core;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace NUnit.Direct
{
    public static class NUnitDirect
    {
        public static void RunTestsOnAssembly(Assembly assembly)
        {
            var package = new TestPackage(assembly.Location);
            if (!CoreExtensions.Host.Initialized)
                CoreExtensions.Host.InitializeService();

            var testsIndirect = new TestSuiteBuilder().Build(package);
            var tests = directize(testsIndirect);

            var results = new TestResult(tests);
            tests.Run(results, new DirectListener(), NUnit.Core.TestFilter.Empty);
        }

        private static DirectTestSuite directize(TestSuite suite)
        {
            var result = new DirectTestSuite(suite);

            // replace all TestSuite's with DirectTestSuite's recursively... ugh...
            Queue<Test> tests = new Queue<Test>();
            tests.Enqueue(result);
            while (tests.Count > 0)
            {
                var test = tests.Dequeue();
                if (test.Tests == null)
                    continue;
                var subtests = test.Tests.Cast<Test>().ToList();
                test.Tests.Clear();
                foreach (var subtest in subtests)
                {
                    if (subtest is TestSuite)
                    {
                        var replacement = new DirectTestSuite((TestSuite) subtest);
                        test.Tests.Add(replacement);
                        tests.Enqueue(replacement);
                    }
                    else if (subtest is TestMethod)
                    {
                        var replacement = new DirectTestMethod((TestMethod) subtest);
                        test.Tests.Add(replacement);
                        tests.Enqueue(replacement);
                    }
                    else
                    {
                        test.Tests.Add(subtest);
                        tests.Enqueue(subtest);
                    }
                }
            }

            return result;
        }
    }

    class DirectListener : EventListener
    {
        private LoggerBase log = new ConsoleLogger();

        public void RunStarted(string name, int testCount)
        {
        }

        public void RunFinished(Exception exception)
        {
            log.Warn("Test run finished with exception:");
            log.Exception(exception);
        }

        public void RunFinished(TestResult result)
        {
            log.Info(result.IsSuccess ? "Test run finished - SUCCESS" : "Test run finished - FAILURE");
            // don't care too much about logging failures because they would most certainly never reach this point,
            // given how exceptions are not swallowed.
        }

        public void SuiteStarted(TestName testName)
        {
            log.Info("Start suite {0}".Fmt(testName.FullName));
        }

        public void SuiteFinished(TestResult result)
        {
            log.Info("End suite {0} ({1}, took {2} sec)".Fmt(result.Test.TestName.FullName, result.ResultState, result.Time));
        }

        public void TestStarted(TestName testName)
        {
            log.Info("Running test {0} ...".Fmt(testName.Name));
        }

        public void TestFinished(TestResult result)
        {
        }

        public void TestOutput(TestOutput testOutput)
        {
            LogType type;
            switch (testOutput.Type)
            {
                case TestOutputType.Error: type = LogType.Error; break;
                case TestOutputType.Out: type = LogType.Info; break;
                default: type = LogType.Debug; break;
            }
            log.Log(0, type, testOutput.Text);
        }

        public void UnhandledException(Exception exception)
        {
            // we don't expect this to ever get invoked
            log.Warn("Unhandled exception:");
            log.Exception(exception);
        }
    }

    #region This was copied & pasted out of NUnit 2.5.3 where overriding alone wasn't enough. "catch" clauses were removed.

    class DirectTestSuite : TestSuite
    {
        public DirectTestSuite(TestSuite suite)
            : base(suite.TestName.Name)
        {
            foreach (var fld in typeof(TestSuite).GetAllFields())
                fld.SetValue(this, fld.GetValue(suite));
            if (Tests != null)
                foreach (Test child in Tests)
                    child.Parent = this;
        }

        protected override void DoOneTimeSetUp(TestResult suiteResult)
        {
            if (FixtureType != null)
            {
                // In case TestFixture was created with fixture object
                if (Fixture == null && !(FixtureType.IsAbstract && FixtureType.IsSealed))
                    CreateUserFixture();

                if (this.Properties["_SETCULTURE"] != null)
                    TestContext.CurrentCulture =
                        new System.Globalization.CultureInfo((string) Properties["_SETCULTURE"]);

                if (this.Properties["_SETUICULTURE"] != null)
                    TestContext.CurrentUICulture =
                        new System.Globalization.CultureInfo((string) Properties["_SETUICULTURE"]);

                if (this.fixtureSetUpMethods != null)
                    foreach (MethodInfo fixtureSetUp in fixtureSetUpMethods)
                        Helper.InvokeMethodDirect(fixtureSetUp, fixtureSetUp.IsStatic ? null : Fixture, new object[0]);
            }
        }

        protected override void DoOneTimeTearDown(TestResult suiteResult)
        {
            if (this.FixtureType != null)
            {
                if (this.fixtureTearDownMethods != null)
                {
                    int index = fixtureTearDownMethods.Length;
                    while (--index >= 0)
                    {
                        MethodInfo fixtureTearDown = fixtureTearDownMethods[index];
                        Helper.InvokeMethodDirect(fixtureTearDown, fixtureTearDown.IsStatic ? null : Fixture, new object[0]);
                    }
                }

                IDisposable disposable = Fixture as IDisposable;
                if (disposable != null)
                    disposable.Dispose();

                this.Fixture = null;
            }
        }
    }

    class DirectTestMethod : TestMethod
    {
        public DirectTestMethod(TestMethod other)
            : base(other.Method)
        {
            foreach (var fld in typeof(TestMethod).GetAllFields())
                fld.SetValue(this, fld.GetValue(other));
            if (Tests != null)
                foreach (Test child in Tests)
                    child.Parent = this;
        }

        public override void doRun(TestResult testResult)
        {
            DateTime start = DateTime.Now;

            try
            {
                doSetUp();

                doTestCase(testResult);
            }
            finally
            {
                doTearDown(testResult);

                DateTime stop = DateTime.Now;
                TimeSpan span = stop.Subtract(start);
                testResult.Time = (double) span.Ticks / (double) TimeSpan.TicksPerSecond;


                if (testResult.IsSuccess && this.Properties.Contains("MaxTime"))
                {
                    int elapsedTime = (int) Math.Round(testResult.Time * 1000.0);
                    int maxTime = (int) this.Properties["MaxTime"];

                    if (maxTime > 0 && elapsedTime > maxTime)
                        testResult.Failure(
                            string.Format("Elapsed time of {0}ms exceeds maximum of {1}ms",
                                elapsedTime, maxTime),
                            null);
                }
            }
        }

        private void doSetUp()
        {
            if (setUpMethods != null)
                foreach (MethodInfo setUpMethod in setUpMethods)
                    Helper.InvokeMethodDirect(setUpMethod, setUpMethod.IsStatic ? null : this.Fixture);
        }

        private void doTearDown(TestResult testResult)
        {
            if (tearDownMethods != null)
            {
                int index = tearDownMethods.Length;
                while (--index >= 0)
                    Helper.InvokeMethodDirect(tearDownMethods[index], tearDownMethods[index].IsStatic ? null : this.Fixture);
            }
        }

        private void doTestCase(TestResult testResult)
        {
            object fixture = this.Method.IsStatic ? null : this.Fixture;

            // Ugh... RunTestMethod is publicly overridable, but depends on a whole bunch of internal/private detail, so it's not really very overridable...
            var arguments = Helper.ReadPrivateField<object[]>(this, "arguments");
            var hasExpectedResult = Helper.ReadPrivateField<bool>(this, "hasExpectedResult");
            var expectedResult = Helper.ReadPrivateField<object>(this, "expectedResult");

            object result = Helper.InvokeMethodDirect(this.Method, fixture, arguments);

            if (hasExpectedResult)
                NUnitFramework.Assert.AreEqual(expectedResult, result);

            testResult.Success();
        }

        public override void Run(TestResult testResult)
        {
            try
            {
                if (this.Parent != null)
                {
                    Test t = this;
                    while (t != null && Fixture == null)
                    {
                        Fixture = t.Fixture;
                        t = t.Parent;
                    }
                    TestSuite suite = this.Parent as TestSuite;
                    if (suite != null)
                    {
                        this.setUpMethods = suite.GetSetUpMethods();
                        this.tearDownMethods = suite.GetTearDownMethods();
                    }
                }

                // Temporary... to allow for tests that directly execute a test case
                if (Fixture == null && !Method.IsStatic)
                    Fixture = Reflect.Construct(this.FixtureType);

                if (this.Properties["_SETCULTURE"] != null)
                    TestContext.CurrentCulture =
                        new System.Globalization.CultureInfo((string) Properties["_SETCULTURE"]);

                if (this.Properties["_SETUICULTURE"] != null)
                    TestContext.CurrentUICulture =
                        new System.Globalization.CultureInfo((string) Properties["_SETUICULTURE"]);

                int repeatCount = this.Properties.Contains("Repeat")
                    ? (int) this.Properties["Repeat"] : 1;

                while (repeatCount-- > 0)
                {
                    if (RequiresThread || Timeout > 0 || ApartmentState != GetCurrentApartment())
                        new TestMethodThread(this).Run(testResult, NullListener.NULL, TestFilter.Empty);
                    else
                        doRun(testResult);

                    if (testResult.ResultState == ResultState.Failure ||
                        testResult.ResultState == ResultState.Error ||
                        testResult.ResultState == ResultState.Cancelled)
                    {
                        break;
                    }
                }

            }
            finally
            {
                Fixture = null;
            }
        }
    }

    #endregion
}
