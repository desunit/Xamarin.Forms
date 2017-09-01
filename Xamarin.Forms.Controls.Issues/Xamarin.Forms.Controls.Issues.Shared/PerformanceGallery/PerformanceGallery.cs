﻿using Plugin.DeviceInfo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xamarin.Forms.CustomAttributes;
using Xamarin.Forms.Internals;

#if UITEST
using Xamarin.UITest;
using NUnit.Framework;
#endif

namespace Xamarin.Forms.Controls.Issues
{
#if UITEST
	[Category(Xamarin.Forms.Core.UITests.UITestCategories.Performance)]
#endif

	[Preserve(AllMembers = true)]
	[Issue(IssueTracker.None, 0, "Performance Testing")]
	public class PerformanceGallery : TestContentPage
	{
		const string Fail = "FAIL";
		const string Next = "Next Scenario";
		const string Pending = "PENDING";
		const string Success = "SUCCESS";
		const double Threshold = 0.25;

		string _DeviceIdentifier = "";
		string _DeviceIdiom;
		string _DeviceModel;
		string _DevicePlatform;
		string _DeviceVersionNumber;
		PerformanceProvider _PerformanceProvider = new PerformanceProvider();
		PerformanceTracker _PerformanceTracker = new PerformanceTracker();
		List<PerformanceScenario> _TestCases = new List<PerformanceScenario>();
		int _TestNumber = 0;
		Guid _TestRunReferenceId;

		PerformanceViewModel ViewModel => BindingContext as PerformanceViewModel;

		protected override async void Init()
		{
			_TestRunReferenceId = Guid.NewGuid();

			_DeviceIdiom = CrossDeviceInfo.Current.Idiom.ToString();
			_DeviceModel = CrossDeviceInfo.Current.Model;
			_DevicePlatform = CrossDeviceInfo.Current.Platform.ToString();
			_DeviceVersionNumber = CrossDeviceInfo.Current.VersionNumber.ToString();

			MessagingCenter.Subscribe<PerformanceTracker>(this, PerformanceTracker.RenderCompleteMessage, HandleRenderComplete);

			BindingContext = new PerformanceViewModel(_PerformanceProvider);
			Performance.SetProvider(_PerformanceProvider);

			_TestCases.AddRange(InflatePerformanceScenarios());

			var nextButton = new Button { Text = Pending, IsEnabled = false };
			nextButton.Clicked += NextButton_Clicked;

			Content = new StackLayout { Children = { nextButton, _PerformanceTracker } };

			ViewModel.BenchmarkResults = await PerformanceDataManager.GetScenarioResults(_DevicePlatform);

			nextButton.IsEnabled = true;
			nextButton.Text = Next;
		}

		static IEnumerable<Type> FindPerformanceScenarios()
		{
			return typeof(PerformanceGallery).GetTypeInfo().Assembly.DefinedTypes.Select(o => o.AsType())
													.Where(typeInfo => typeof(PerformanceScenario).IsAssignableFrom(typeInfo));
		}

		static IEnumerable<PerformanceScenario> InflatePerformanceScenarios()
		{
			var scenarios = FindPerformanceScenarios()
							.Select(o => (PerformanceScenario)Activator.CreateInstance(o))
							.Where(scenario => scenario.View != null);

			if (scenarios.GroupBy(c => c.Name).Any(c => c.Count() > 1))
				throw new InvalidOperationException("Scenario names must be unique");

			return scenarios;
		}

		PerformanceDataManager.Result DisplayResults()
		{
			ViewModel.ActualRenderTime = TimeSpan.FromTicks(_PerformanceProvider.Statistics.Where(c => !c.Value.IsDetail).Sum(c => c.Value.TotalTime)).TotalMilliseconds;

			// perf should be within threshold
			if (ViewModel.ExpectedRenderTime == 0)
			{
				ViewModel.Outcome = Fail;
				return PerformanceDataManager.Result.Inconclusive;
			}
			else if (Math.Abs(ViewModel.ActualRenderTime - ViewModel.ExpectedRenderTime) > ViewModel.ExpectedRenderTime * Threshold)
			{
				ViewModel.Outcome = Fail;
				return PerformanceDataManager.Result.Fail;
			}
			else
			{
				ViewModel.Outcome = Success;
				return PerformanceDataManager.Result.Pass;
			}
		}

		void HandleRenderComplete(PerformanceTracker obj)
		{
			var result = DisplayResults();

			PerformanceDataManager.PostScenarioResults(ViewModel.Scenario, 
				result, 
				_TestRunReferenceId, 
				_DeviceIdentifier, 
				_DevicePlatform, 
				_DeviceVersionNumber, 
				_DeviceIdiom, 
				ViewModel.ActualRenderTime, 
				_PerformanceProvider.Statistics);
		}

		void NextButton_Clicked(object sender, EventArgs e)
		{
			if (_TestCases?.Count == 0 || _TestNumber + 1 > _TestCases?.Count)
				return;

			ViewModel.View = null;
			ViewModel.ActualRenderTime = 0;
			ViewModel.Outcome = Pending;
			ViewModel.RunTest(_TestCases[_TestNumber++]);
		}

#if UITEST

		double TopThreshold => 1 + Threshold;
		double BottomThreshold => 1 - Threshold;

		[Test]
		public void PerformanceTest()
		{
			_DeviceIdentifier = RunningApp.Device.DeviceIdentifier;

			var testCasesCount = FindPerformanceScenarios().Count();

			List<string> warnings = new List<string>();

			for (int i = 0; i < testCasesCount; i++)
			{
				RunningApp.WaitForElement(q => q.Marked(Next));
				RunningApp.Tap(q => q.Marked(Next));

				try
				{
					RunningApp.WaitForElement(q => q.Marked(Success));
				}
				catch (Exception)
				{
					var message = GetFailureMessage();
					if (!warnings.Contains(message))
						warnings.Add(message);
				}
			}

			if (warnings.Any())
				Assert.Inconclusive($"Performance threshold exceeded.\r\n{string.Join("\r\n", warnings)}");
		}

		string GetFailureMessage()
		{
			double expected = 0;
			double.TryParse(GetText(PerformanceTrackerTemplate.ExpectedId), out expected);

			var scenario = GetText(PerformanceTrackerTemplate.ScenarioId);
			var actual = GetText(PerformanceTrackerTemplate.ActualId);

			return $" - Scenario \"{scenario}\" failed. Expected {expected * BottomThreshold}-{expected * TopThreshold}ms, Actual {actual}ms.";
		}

		string GetText(string id)
		{
			return RunningApp.Query(q => q.Marked(id))[0].Text;
		}
#endif
	}
}