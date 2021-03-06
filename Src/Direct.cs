﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Core;

namespace NUnit.Direct
{
    /// <summary>Provides a method to run NUnit-compatible unit tests in an assembly in a debugger-friendly way.</summary>
    public static class NUnitDirect
    {
        /// <summary>
        ///     Runs NUnit-compatible unit tests in an assembly in a debugger-friendly way.</summary>
        /// <param name="assembly">
        ///     The assembly containing the unit tests to run.</param>
        /// <param name="suppressTimesInLog">
        ///     Indicates whether to suppress the timing information in the log output produced. Defaults to <c>false</c>.</param>
        /// <param name="filter">
        ///     If not <c>null</c> (the default), only tests that match this regular expression are run.</param>
        public static void RunTestsOnAssembly(Assembly assembly, bool suppressTimesInLog = false, string filter = null)
        {
            var package = new TestPackage(assembly.Location);
            if (!CoreExtensions.Host.Initialized)
                CoreExtensions.Host.InitializeService();

            var testsIndirect = new TestSuiteBuilder().Build(package);
            var tests = directize(testsIndirect, filter);

            var results = new TestResult(tests);
            tests.Run(results, new DirectListener(suppressTimesInLog), TestFilter.Empty);
        }

        /// <summary>
        ///     Invokes method <paramref name="method"/> on object <paramref name="instance"/> passing it the specified
        ///     <paramref name="parameters"/>. Unlike <see cref="MethodBase.Invoke(object, object[])"/>, which wraps any
        ///     exceptions in the target method into a <see cref="TargetInvocationException"/>, this will invoke the target
        ///     method in such a way that any exceptions will propagate just like they would if the method had been invoked
        ///     "directly" rather than via reflection. See Remarks.</summary>
        /// <remarks>
        ///     There is a good reason why <see cref="TargetInvocationException"/> is used: when using this method, you cannot
        ///     tell apart an exception that occurred in the invoker (e.g. you passed in a null for <paramref name="method"/>
        ///     or wrong number of parameter) from an exception that occurred in the target method. But it also means that the
        ///     debugger will always stop at the Invoke call and not at the actual exception, because the inner exception is
        ///     considered handled. One solution to this is to configure VS to stop on all exceptions, which has its own
        ///     downsides. The other one is to use <see cref="InvokeDirect"/>. In practice, distinguishing target exceptions
        ///     from invoker exceptions is usually not very important, so the annoyance of having to debug exceptions
        ///     differently becomes a bigger problem.</remarks>
        /// <param name="method">
        ///     The method to invoke. Must be an instance method.</param>
        /// <param name="instance">
        ///     The instance on which to invoke the method. Must not be null, as static methods are not supported.</param>
        /// <param name="parameters">
        ///     Parameters to pass into the method.</param>
        /// <returns>
        ///     What the target method returned, or null if its return type is void.</returns>
        public static object InvokeDirect(this MethodInfo method, object instance, params object[] parameters)
        {
            return Helper.InvokeMethodDirect(method, instance, parameters);
        }

        private static DirectTestSuite directize(TestSuite suite, string filter)
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
                        if (filter == null || Regex.IsMatch(subtest.TestName.FullName, filter))
                        {
                            var replacement = new DirectTestMethod((TestMethod) subtest);
                            test.Tests.Add(replacement);
                            tests.Enqueue(replacement);
                        }
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
        private bool _suppressTimesInLog;

        public DirectListener(bool suppressTimesInLog)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _suppressTimesInLog = suppressTimesInLog;
        }

        public void RunStarted(string name, int testCount)
        {
        }

        public void RunFinished(Exception exception)
        {
            log("Test run finished with exception:");
            logException(exception);
        }

        public void RunFinished(TestResult result)
        {
            log(result.IsSuccess ? "Test run finished - SUCCESS" : "Test run finished - FAILURE");
            // don't care too much about logging failures because they would most certainly never reach this point,
            // given how exceptions are not swallowed.
        }

        public void SuiteStarted(TestName testName)
        {
            log("{0} — START".Fmt(testName.FullName));
        }

        public void SuiteFinished(TestResult result)
        {
            log((_suppressTimesInLog ? "{0} — END ({1})" : "{0} — END ({1}, took {2} sec)").Fmt(result.Test.TestName.FullName, result.ResultState, result.Time));
        }

        public void TestStarted(TestName testName)
        {
            log(" • {0}".Fmt(testName.Name));
        }

        public void TestFinished(TestResult result)
        {
        }

        public void TestOutput(TestOutput testOutput)
        {
            log(testOutput.Text);
        }

        public void UnhandledException(Exception exception)
        {
            // we don't expect this to ever get invoked
            log("Unhandled exception:");
            logException(exception);
        }

        private void log(string str)
        {
            if (!_suppressTimesInLog)
                Console.Write("{0:yyyy'-'MM'-'dd' 'HH':'mm':'ss'.'fff} | ", DateTime.Now);
            Console.WriteLine(str);
        }

        private void logException(Exception exception)
        {
            if (exception.InnerException != null)
                logException(exception.InnerException);

            log("<{0}>: {1}".Fmt(exception.GetType().ToString(), exception.Message));
            log(exception.StackTrace);
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
