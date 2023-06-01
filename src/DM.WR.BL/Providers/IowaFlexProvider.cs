﻿using DM.WR.BL.Builders;
using DM.WR.BL.Managers;
using DM.WR.GraphQlClient;
using DM.WR.Models.Config;
using DM.WR.Models.GraphqlClient.UserEndPoint;
using DM.WR.Models.IowaFlex;
using DM.WR.Models.Types;
using DM.WR.Models.ViewModels;
using HandyStuff;
using NLog;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using DM.WR.Models.GraphqlClient.PerformanceLevelDescriptorsEndPoint;
using DM.WR.Models.IowaFlex.ViewModels;
using PerformanceLevel = DM.WR.Models.GraphqlClient.DomainEndPoint.PerformanceLevel;

namespace DM.WR.BL.Providers
{
    public interface IIowaFlexProvider
    {
        DashboardIowaFlexViewModel BuildPageViewModel(string enableQueryLogging);
        Task<IowaFlexFiltersViewModel> GetFiltersAsync(string appPath, string isCogat);
        Task UpdateFiltersAsync(string filterTypeNumber, List<string> values, string appPath);
        void ResetFilters();
        Task GoToRootNodeAsync();
        Task DrillDownLocationsPathAsync(LocationNode node);
        Task DrillUpLocationsPathAsync(LocationNode node);
        Task<dynamic> GetTestScoresAsync(string appPath, string performanceBand, string domainId, string domainLevel, string isCogat);
        Task<dynamic> GetDomainsAsync(string appPath, string bandId, string isCogat);
        Task<dynamic> GetRosterAsync(string appPath);
        Task<dynamic> GetStudentRosterAsync(string appPath, string performanceBand, string domainId, string domainLevel);
        Task<IowaFlexProfileNarrativeViewModel> GetProfileNarrativeAsync(string userIds);
        Task<PerformanceScoresKto1Model> GetPerformanceScoresKto1Async();
        Task<DonutCardsKto1Model> GetPerformanceDonutsKto1Async(string pldStage, int? pldLevel);
        Task<RosterKto1Model> GetRosterKto1Async(string appPath, string pldStage, int? pldLevel);
        Task<List<ProfileNarrativeKto1ViewModel>> GetProfileNarrativeKto1Async(string studentIds);
        Task<DifferentiatedReportKto1HierarchyViewModel> GetDifferentiatedReportHierarchyKto1Async();
        Task<DifferentiatedReportKto1ViewModel> GetDifferentiatedReportKto1Async(string studentIds);
        Task<CogatRosterModel> GetCogatLocationRosterAsync(string appPath, int? performanceBand, int? domainId, int? domainLevel, int? cogatAbility, string cogatScore);
        Task<CogatRosterModel> GetCogatStudentRosterAsync(string appPath, int? performanceBand, int? domainId, int? domainLevel, int? cogatAbility, string cogatScore, string contentName);
        Task<PerformanceLevelMatrixModel> GetPerformanceLevelMatrix(string contentType, string contentName, string performanceBand, string domainId, string domainLevel);
    }

    public class IowaFlexProvider : IIowaFlexProvider
    {
        private readonly IApiClient _adaptiveApiClient;
        private readonly IIowaFlexFiltersBuilder _filtersBuilder;
        private readonly IDashboardIowaFlexProviderBuilder _dashboardIowaFlexProviderBuilder;
        private readonly IGraphQlQueryStringBuilder _graphQlQueryStringBuilder;
        private readonly ISessionManager _sessionManager;

        private readonly UserData _userData;

        private readonly CommonProviderFunctions _commonFunctions;
        private readonly IIowaFlexCommonProviderFunctions _commonFlexFunctions;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public IowaFlexProvider(IApiClient adaptiveApiClient, IIowaFlexFiltersBuilder filtersBuilder, IDashboardIowaFlexProviderBuilder dashboardIowaFlexProviderBuilder, IGraphQlQueryStringBuilder graphQlQueryStringBuilder, ISessionManager sessionManager, IUserDataManager userDataManager, IIowaFlexCommonProviderFunctions commonFlexFunctions)
        {
            _adaptiveApiClient = adaptiveApiClient;
            _filtersBuilder = filtersBuilder;
            _dashboardIowaFlexProviderBuilder = dashboardIowaFlexProviderBuilder;
            _graphQlQueryStringBuilder = graphQlQueryStringBuilder;
            _sessionManager = sessionManager;

            _userData = userDataManager.GetUserData();

            _commonFunctions = new CommonProviderFunctions();
            _commonFlexFunctions = commonFlexFunctions;
        }

        public DashboardIowaFlexViewModel BuildPageViewModel(string enableQueryLogging)
        {
            if (enableQueryLogging != null && enableQueryLogging.ToBoolean())
                _sessionManager.Store(true, SessionKey.EnableQuerryLogging);
            else
                _sessionManager.Delete(SessionKey.EnableQuerryLogging);

            return new DashboardIowaFlexViewModel
            {
                IsAdaptive = _userData.IsAdaptive,
                IsDemo = _userData.IsDemo,
                IsGuidUser = _commonFunctions.IsGuidUser(_userData),
                IsProd = ConfigSettings.IsEnvironmentProd
            };
        }

        public async Task<IowaFlexFiltersViewModel> GetFiltersAsync(string appPath, string isCogat)
        {
            var filterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            bool isRecreateFilters = false;
            if (filterPanel != null && filterPanel.IsCogat != !string.IsNullOrEmpty(isCogat)) {
                if (!string.IsNullOrEmpty(isCogat)) filterPanel.IsCogat = true;
                else filterPanel.IsCogat = false;
                _sessionManager.Store(filterPanel, SessionKey.IowaFlexFilters);
                isRecreateFilters = true;
            }

            if (filterPanel == null || isRecreateFilters)
            {
                var query = "";
                var filterType = FilterType._INTERNAL_FIRST_;
                if (filterPanel == null)
                {
                    filterPanel = new IowaFlexFilterPanel
                    {
                        RootNodes = _userData.CustomerInfoList.Select(l => new LocationNode { NodeId = Convert.ToInt32((l.NodeId)), NodeType = l.NodeType }).ToList(),
                    };
                }
                else
                {
                    filterType = filterPanel.LastUpdatedFilterType;
                }
                query = _graphQlQueryStringBuilder.BuildFiltersQueryString(filterPanel, filterType, _userData.UserId);
                Logger.Info($"Query Logging :: Default Filters :: {query}");
                var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(query);

                filterPanel = _filtersBuilder.BuildFilters(filterPanel, apiResponse, filterType);
                if (isRecreateFilters)
                {
                    if (!string.IsNullOrEmpty(isCogat)) filterPanel.IsCogat = true;
                    else filterPanel.IsCogat = false;
                } else
                {
                    filterPanel.BreadCrumbs = new List<LocationNode> { _commonFlexFunctions.MakeRootBreadCrumb(filterPanel.GetFilterByType(FilterType.ParentLocations)) };
                }

                filterPanel.GraphqlQuery = ConfigSettings.IsEnvironmentProd ? "" : query;

                _sessionManager.Store(filterPanel, SessionKey.IowaFlexFilters);
            }

            var kto1Grades = new[] { "0", "K", "k", "1" };
            var isKto1 = kto1Grades.Contains(filterPanel.GetSelectedValuesStringOf(FilterType.Grade));
            var rootNodeLevel = filterPanel.RootNodes.First().NodeType;
            var hasDifferentiatedKto1Report = isKto1 && (rootNodeLevel.ToLower() == "building" || rootNodeLevel.ToLower() == "class");

            return new IowaFlexFiltersViewModel
            {
                Filters = filterPanel.GetAllFilters(),
                LocationsBreadCrumbs = _commonFlexFunctions.MakeBreadCrumbs(filterPanel, $"{appPath}/api/Dashboard"),
                RootLocationLevel = rootNodeLevel,
                IsKto1 = isKto1,
                HasDifferentiatedKto1Report = hasDifferentiatedKto1Report,
                GraphqlQuery = filterPanel.GraphqlQuery
            };
        }

        public async Task<IowaFlexFiltersViewModel> GetFiltersAsyncElevate(string appPath, string isCogat)
        {
            var filterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            bool isRecreateFilters = false;
            if (filterPanel != null && filterPanel.IsCogat != !string.IsNullOrEmpty(isCogat))
            {
                if (!string.IsNullOrEmpty(isCogat)) filterPanel.IsCogat = true;
                else filterPanel.IsCogat = false;
                _sessionManager.Store(filterPanel, SessionKey.IowaFlexFilters);
                isRecreateFilters = true;
            }

            if (filterPanel == null || isRecreateFilters)
            {
                var query = "";
                var filterType = FilterType._INTERNAL_FIRST_;
                if (filterPanel == null)
                {
                    filterPanel = new IowaFlexFilterPanel
                    {
                        RootNodes = _userData.CustomerInfoList.Select(l => new LocationNode { NodeId = Convert.ToInt32(l.NodeId), NodeType = l.NodeType }).ToList(), //selecting DISTRIC,CLASS 
                    };
                }
                else
                {
                    filterType = filterPanel.LastUpdatedFilterType;
                }
                query = _graphQlQueryStringBuilder.BuildFiltersQueryString(filterPanel, filterType, _userData.UserId);
                Logger.Info($"Query Logging :: Default Filters :: {query}");
                var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(query);

                filterPanel = _filtersBuilder.BuildFilters(filterPanel, apiResponse, filterType);
                if (isRecreateFilters)
                {
                    if (!string.IsNullOrEmpty(isCogat)) filterPanel.IsCogat = true;
                    else filterPanel.IsCogat = false;
                }
                else
                {
                    filterPanel.BreadCrumbs = new List<LocationNode> { _commonFlexFunctions.MakeRootBreadCrumb(filterPanel.GetFilterByType(FilterType.ParentLocations)) };
                }

                filterPanel.GraphqlQuery = ConfigSettings.IsEnvironmentProd ? "" : query;

                _sessionManager.Store(filterPanel, SessionKey.IowaFlexFilters);
            }

            var kto1Grades = new[] { "0", "K", "k", "1" };
            var isKto1 = kto1Grades.Contains(filterPanel.GetSelectedValuesStringOf(FilterType.Grade));
            var rootNodeLevel = filterPanel.RootNodes.First().NodeType;
            var hasDifferentiatedKto1Report = isKto1 && (rootNodeLevel.ToLower() == "building" || rootNodeLevel.ToLower() == "class");

            return new IowaFlexFiltersViewModel
            {
                Filters = filterPanel.GetAllFilters(),
                LocationsBreadCrumbs = _commonFlexFunctions.MakeBreadCrumbs(filterPanel, $"{appPath}/api/Dashboard"),
                RootLocationLevel = rootNodeLevel,
                IsKto1 = isKto1,
                HasDifferentiatedKto1Report = hasDifferentiatedKto1Report,
                GraphqlQuery = filterPanel.GraphqlQuery
            };
        }

        public async Task UpdateFiltersAsync(string filterTypeNumber, List<string> values, string appPath)
        {
            var currentPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);
            Enum.TryParse(filterTypeNumber, out FilterType filterType);

            currentPanel = _commonFlexFunctions.ChangeFiltersSelection(currentPanel, filterType, values);

            if (filterType == FilterType.TestEvent)
            {
                currentPanel.IsCogat = false;
            }

            var newPanel = await _commonFlexFunctions.RecreateFiltersAsync(currentPanel, filterType, _userData.UserId);

            newPanel.LastUpdatedFilterType = filterType;

            if (filterType <= FilterType.ParentLocations)
                newPanel.BreadCrumbs = new List<LocationNode> { _commonFlexFunctions.MakeRootBreadCrumb(newPanel.GetFilterByType(FilterType.ParentLocations)) };

            _sessionManager.Store(newPanel, SessionKey.IowaFlexFilters);
        }

        public void ResetFilters()
        {
            _sessionManager.Delete(SessionKey.IowaFlexFilters);
        }

        public async Task GoToRootNodeAsync()
        {
            var filterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            filterPanel.BreadCrumbs = null;

            var newPanel = await _commonFlexFunctions.RecreateFiltersAsync(filterPanel, FilterType.Grade, _userData.UserId);
            newPanel.BreadCrumbs = new List<LocationNode> { _commonFlexFunctions.MakeRootBreadCrumb(newPanel.GetFilterByType(FilterType.ParentLocations)) };

            _sessionManager.Store(newPanel, SessionKey.IowaFlexFilters);
        }

        public async Task DrillDownLocationsPathAsync(LocationNode node)
        {
            var currentPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);
            currentPanel = _commonFlexFunctions.DrillDownLocationsPathAsync(currentPanel, node);

            var newPanel = await _commonFlexFunctions.RecreateFiltersAsync(currentPanel, FilterType.ParentLocations, _userData.UserId);

            _sessionManager.Store(newPanel, SessionKey.IowaFlexFilters);
        }

        public async Task DrillUpLocationsPathAsync(LocationNode node)
        {
            var currentPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);
            currentPanel = _commonFlexFunctions.DrillUpLocationsPathAsync(currentPanel, node);

            var newPanel = await _commonFlexFunctions.RecreateFiltersAsync(currentPanel, FilterType.ParentLocations, _userData.UserId);

            _sessionManager.Store(newPanel, SessionKey.IowaFlexFilters);
        }

        public async Task<dynamic> GetTestScoresAsync(string appPath, string performanceBand, string domainId, string domainLevel, string isCogat)
        {
            var filterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildTestScoresQueryString(filterPanel, _userData.UserId, performanceBand, domainId, domainLevel, isCogat);
            var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(query);

            if (apiResponse == null)
                return new { nodata = true };

            dynamic result = new ExpandoObject();
            var testScore = apiResponse.TestEvents.First().TestScore;

            if (testScore == null)
                return new { nodata = true };

            var scores = testScore.Scores;
            var bands = scores.First().PerformanceBands;

            result.graph_ql_query = ConfigSettings.IsEnvironmentProd ? "" : query;

            result.title = "Percent of Students in each Quantile Range";
            result.category = testScore.Subject;
            result.average_standard_score = testScore.StandardScore;
            result.national_percentile_rank = scores.First().Value;
            result.url = $"{appPath}/api/Dashboard/GetStudentRoster";
            result.is_longitudinal = apiResponse.TestEvents.First().IsLongitudinal;
            result.is_cogat = ConfigSettings.IsIowaFlexCogatEnabled && apiResponse.TestEvents.First().IsCogat;

            var values = new List<dynamic>();
            foreach (var item in bands)
            {
                dynamic band = new ExpandoObject();
                band.caption = item.Name;
                band.color = "";
                band.number = item.NumberOfStudents;
                band.percent = item.Percent;
                band.range_band = $"{item.Lower}:{item.Upper}";
                band.url_params = $"performanceBand={item.Id}";
                band.range = item.Id;
                band.average_standard_score = item.StandardScore;
                band.national_percentile_rank = item.Npr;

                values.Add(band);
            }
            result.values = values;

            return result;
        }

        public async Task<dynamic> GetDomainsAsync(string appPath, string bandId, string isCogat)
        {
            var filterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildDomainsQueryString(filterPanel, bandId, isCogat, _userData.UserId);
            var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(query);

            if (apiResponse == null)
                return new { nodata = true };

            var domains = apiResponse.TestEvents.First().DomainScores;

            if (domains == null || !domains.Any())
                return new { nodata = true };

            var result = new List<dynamic>();

            dynamic tempResult = new ExpandoObject();
            tempResult.graph_ql_query = ConfigSettings.IsEnvironmentProd ? "" : query;
            tempResult.cards = result;

            foreach (var domain in domains)
            {
                dynamic card = new ExpandoObject();
                card.title = domain.Description;
                card.url = $"{appPath}/api/Dashboard/GetStudentRoster";

                var performanceLevels = domain.PerformanceLevels;
                var values = new List<dynamic>();
                foreach (var performanceLevel in performanceLevels)
                {
                    dynamic band = new ExpandoObject();
                    band.caption = performanceLevel.Description;
                    band.number = performanceLevel.NumberOfStudents;
                    band.percent = performanceLevel.Percent;
                    band.url_params = $"domainId={domain.Id}&domainLevel={performanceLevel.Id}";
                    band.range = performanceLevel.Id;
                    band.range_band = $"{performanceLevel.Id}:{performanceLevel.Id}";

                    values.Add(band);
                }

                card.values = values;

                result.Add(card);
            }

            return tempResult;
        }

        public async Task<dynamic> GetRosterAsync(string appPath)
        {
            var filterPnel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var rosterQuery = filterPnel.IsChildLocationStudent ?
                _graphQlQueryStringBuilder.BuildStudentRosterQueryString(filterPnel, _userData.UserId) :
                _graphQlQueryStringBuilder.BuildRosterQueryString(filterPnel, _userData.UserId);

            var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(rosterQuery);

            if (apiResponse == null)
                return new { nodata = true };

            return filterPnel.IsChildLocationStudent ?
                BuildStudentRoster(apiResponse, rosterQuery) :
                BuildRoster(appPath, filterPnel, apiResponse, rosterQuery);
        }

        public async Task<dynamic> GetStudentRosterAsync(string appPath, string performanceBand, string domainId, string domainLevel)
        {
            var currentFilterPanel = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var rosterQuery = _graphQlQueryStringBuilder.BuildStudentRosterQueryString(currentFilterPanel, _userData.UserId, performanceBand, domainId, domainLevel);
            var apiResponse = await _adaptiveApiClient.MakeUserCallAsync(rosterQuery);

            if (apiResponse == null)
                return new { nodata = true };

            return BuildStudentRoster(apiResponse, rosterQuery);
        }

        public async Task<IowaFlexProfileNarrativeViewModel> GetProfileNarrativeAsync(string userIds)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var reports = new List<IowaFlexProfileNarrativeReport>();
            var bands = new Dictionary<string, List<Band>>();
            var userIdsList = userIds.Split(',');
            foreach (var id in userIdsList)
            {
                var query = _graphQlQueryStringBuilder.BuildProfileNarrativeQueryString(currentFilters, id);
                var studentData = await _adaptiveApiClient.MakeStudentCallAsync(query);

                var domainLookupQuery = _graphQlQueryStringBuilder.BuildProfileNarrativeLookupQueryString(studentData.CurrentTestEvent.Subject, studentData.CurrentTestEvent.Grade.Name);
                var subjectGradeDomains = await _adaptiveApiClient.MakeProfileNarrativeLookupCallAsync(domainLookupQuery, studentData.CurrentTestEvent.Subject, studentData.CurrentTestEvent.Grade.Name);

                var domainNarratives = new List<IowaFlexProfileNarrativeDomainModel>();
                foreach (var domainScore in studentData.CurrentTestEvent.DomainScores)
                {
                    if (domainScore.PerformanceLevels == null || domainScore.PerformanceLevels.Any() == false)
                    {
                        domainNarratives.Add(new IowaFlexProfileNarrativeDomainModel { ErrorMessage = "Error.  Bad data." });
                        continue;
                    }

                    DomainModel domainModel = new DomainModel();

                    foreach (var domain in subjectGradeDomains.Domains)
                    {
                        if (domain.Id == domainScore.Id)
                        {
                            domainModel = new DomainModel
                            {
                                Id = domain.Id,
                                Name = domain.Name,
                                PerformanceText = PerformanceLevelText(subjectGradeDomains.PerformanceLevels, domainScore.PerformanceLevels.First().Id),
                                Text = domain.Text
                            };
                            break;
                        }
                    }

                    domainNarratives.Add(_dashboardIowaFlexProviderBuilder.ToAdaptiveProfileNarrativeDomainModel(domainModel, domainScore.PerformanceLevels.First().Id, studentData.Name.FirstName));
                }

                var report = _dashboardIowaFlexProviderBuilder.ToAdaptiveProfileNarrativeViewModel(studentData, subjectGradeDomains.Subject.SubjectAbbreviation, domainNarratives);

                foreach (var testEvent in studentData.TestEvents)
                {
                    report.TestEvents.Add(new IowaFlexProfileNarrativeTestEvent
                    {
                        Id = testEvent.TestEventId,
                        Name = testEvent.TestEventName,
                        Date = testEvent.TestDate,
                        Grade = testEvent.Grade.Name,
                        Subject = testEvent.Subject,
                        StandardScore = testEvent.TestScore.StandardScore
                    });

                    //ranges
                    if (bands.ContainsKey(testEvent.Grade.Name))
                        continue;

                    var bandsLookUpQuery = _graphQlQueryStringBuilder.BuildStandardScoreRangeQueryString(testEvent.Grade.Name, testEvent.Subject);
                    var bandsRanges = await _adaptiveApiClient.MakeBandsLookupCallAsync(bandsLookUpQuery, currentFilters.GetSubject(), currentFilters.GetSelectedValuesStringOf(FilterType.Grade));
                    bands.Add(testEvent.Grade.Name, _commonFlexFunctions.BuildBands(bandsRanges));
                }

                report.GraphqlQuery = ConfigSettings.IsEnvironmentProd ? "" : query;
                report.GraphqlLookUpQuery = ConfigSettings.IsEnvironmentProd ? "" : domainLookupQuery;

                reports.Add(report);
            }

            return new IowaFlexProfileNarrativeViewModel
            {
                Reports = reports,
                Ranges = bands
            };
        }

        public async Task<PerformanceScoresKto1Model> GetPerformanceScoresKto1Async()
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildPerformanceScoresKto1QueryString(currentFilters, _userData.UserId);
            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var performanceScores = user.TestEvents.First().RosterCard.PerformanceScoreGraph;

            var model = new PerformanceScoresKto1Model
            {
                GraphQlQuery = query,
                IsLongitudinal = user.TestEvents.First().IsLongitudinal,
                IsCogat = user.TestEvents.First().IsCogat,
                Subject = performanceScores.Subject,
                TotalCount = performanceScores.TotalCount,
                PldValues = new List<PerformanceScoresLevelKto1>()
            };

            foreach (var pldStage in performanceScores.PldStages)
            {
                model.PldValues.Add(new PerformanceScoresLevelKto1
                {
                    Percent = pldStage.Percent,
                    PldStage = pldStage.PldStage,
                    PldStageNum = pldStage.PldStageNum,
                    StudentCount = pldStage.StudentCount
                });
            }

            return model;
        }

        public async Task<DonutCardsKto1Model> GetPerformanceDonutsKto1Async(string pldStage, int? pldLevel)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildPerformanceDonutsKto1QueryString(currentFilters, pldStage, pldLevel, _userData.UserId);
            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var model = new DonutCardsKto1Model
            {
                Cards = new List<DonutCardKto1>(),
                GraphQlQuery = query
            };

            var performanceLevelDonuts = user.TestEvents.First().RosterCard.PerformanceLevelDonuts;

            foreach (var apiDonut in performanceLevelDonuts)
            {
                var donutCard = model.Cards.FirstOrDefault(d => d.PldStage == apiDonut.PldStage);

                if (donutCard == null)
                {
                    donutCard = new DonutCardKto1 { PldStage = apiDonut.PldStage, CardLevels = new List<DonutCardLevelKto1>() };
                    model.Cards.Add(donutCard);
                }

                donutCard.CardLevels.Add(new DonutCardLevelKto1
                {
                    StudentCount = apiDonut.StudentCount,
                    Percent = apiDonut.Percent,
                    PldLevel = apiDonut.PldLevel
                });
            }

            return model;
        }

        public async Task<PerformanceLevelMatrixModel> GetPerformanceLevelMatrix(string contentType, string contentName, string performanceBand, string domainId, string domainLevel)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildPerformanceLevelMatrixQueryString(currentFilters, _userData.UserId, contentType, contentName, performanceBand, domainId, domainLevel);

            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var model = new PerformanceLevelMatrixModel
            {
                GraphQlQuery = query,
                DataPoints = new List<PerformanceLevelMatrixDataPointModel>()
            };

            if (user.TestEvents != null && user.TestEvents.Count > 0)
            {
                var performanceLevelMatrix = user.TestEvents.First().PerformanceLevelMatrix;

                if (performanceLevelMatrix != null && performanceLevelMatrix.DataPoints != null)
                {
                    foreach (var datapoint in performanceLevelMatrix.DataPoints)
                    {
                        model.DataPoints.Add(new PerformanceLevelMatrixDataPointModel()
                        {
                            AbilityAchievement = datapoint.AbilityAchievement,
                            StudentCount = datapoint.StudCount
                        });
                    }
                }
            }

            return model;
        }

        public async Task<RosterKto1Model> GetRosterKto1Async(string appPath, string pldName, int? pldLevel)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildRosterKto1QueryString(currentFilters, pldName, pldLevel, _userData.UserId);
            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var isStudentRoster = currentFilters.IsChildLocationStudent || !string.IsNullOrEmpty(pldName);
            var rosterLevel = isStudentRoster ? "Student" : ((LocationsFilter)currentFilters.GetFilterByType(FilterType.ChildLocations)).LocationNodeType;

            var model = new RosterKto1Model
            {
                GraphQlQuery = query,
                RosterType = isStudentRoster ? "students" : "compare",
                RosterLevel = rosterLevel,
                Columns = new List<RosterKto1Column> //columns
                {
                    new RosterKto1Column
                    {
                        Title = isStudentRoster ? "Student Name" : $"{rosterLevel.FirstCharToUpper()} Name",
                        TitleFull = isStudentRoster ? "Student Name" : $"{rosterLevel.FirstCharToUpper()} Name",
                        Field = "node_name"
                    }
                }
            };

            //columns
            model.Columns.AddRange(isStudentRoster ? BuildStudentRosterKto1Columns() : BuildLocationRosterKto1Columns());

            //values
            if (isStudentRoster)
                model.Values = BuildStudentRosterKto1Values(user.TestEvents.First().RosterCard.Roster.RosterList);
            else
                model.Values = BuildLocationRosterKto1Values(user.TestEvents.First().RosterCard.Roster.RosterList, appPath);

            if (!string.IsNullOrEmpty(pldName))
                model.PerformanceLevelDescriptor = await GetPerformanceLevelDescriptorKto1(pldName);

            if (pldLevel != null)
                model.PerformanceLevelStatement = await GetPerformanceLevelStatementKto1(pldName, pldLevel);

            return model;
        }

        private IEnumerable<RosterKto1Column> BuildStudentRosterKto1Columns()
        {
            return new List<RosterKto1Column>
            {
                new RosterKto1Column {Title = "PLD", TitleFull = "PLD Stage", Field = "PLDS0"},
                new RosterKto1Column {Title = "PLD Level", TitleFull = "PLD Level", Field = "PLDL0"}
            };
        }

        private List<RosterKto1Column> BuildLocationRosterKto1Columns()
        {
            return new List<RosterKto1Column>
            {
                new RosterKto1Column {Title = "Pre-Emerging", TitleFull = "Pre-Emerging", Field = "PE0"},
                new RosterKto1Column {Title = "Emerging", TitleFull = "Emerging", Field = "E0"},
                new RosterKto1Column {Title = "Beginning", TitleFull = "Beginning", Field = "B0"},
                new RosterKto1Column {Title = "Transitioning", TitleFull = "Transitioning", Field = "T0"},
                new RosterKto1Column {Title = "Independent", TitleFull = "Independent", Field = "I0"}
            };
        }

        private List<RosterKto1ValueStudent> BuildStudentRosterKto1Values(List<RosterListKto1> rosterList)
        {
            var values = new List<RosterKto1ValueStudent>();

            foreach (var rosterListKto1 in rosterList)
            {
                values.Add(new RosterKto1ValueStudent
                {
                    Id = rosterListKto1.Id,
                    Name = rosterListKto1.Name,
                    ExternalId = rosterListKto1.ExternalStudentId,
                    Link = "#",
                    PldStage = rosterListKto1.PldStage,
                    PldLevel = rosterListKto1.PldLevel,
                    PldStageNum = rosterListKto1.PldStageNum
                });
            }

            return values;
        }

        private List<RosterKto1ValueLocation> BuildLocationRosterKto1Values(List<RosterListKto1> rosterList, string appPath)
        {
            var values = new List<RosterKto1ValueLocation>();

            foreach (var rosterListKto1 in rosterList)
            {
                values.Add(new RosterKto1ValueLocation
                {
                    Id = rosterListKto1.Id,
                    Name = rosterListKto1.Name,
                    Level = rosterListKto1.Level,
                    Link = $"{appPath}/api/Dashboard/DrillDownLocations?id={rosterListKto1.Id}&name={rosterListKto1.Name}&type={rosterListKto1.Level}",
                    PreEmerging = rosterListKto1.PreEmerging,
                    Emerging = rosterListKto1.Emerging,
                    Beginning = rosterListKto1.Beginning,
                    Transitioning = rosterListKto1.Transitioning,
                    Independent = rosterListKto1.Independent
                });
            }

            return values;
        }

        public async Task<List<ProfileNarrativeKto1ViewModel>> GetProfileNarrativeKto1Async(string studentIds)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var reports = new List<ProfileNarrativeKto1ViewModel>();
            var studentIdsList = studentIds.Split(',');

            foreach (var id in studentIdsList)
            {
                var query = _graphQlQueryStringBuilder.BuildProfileNarrativeKto1QueryString(currentFilters, id);
                var student = await _adaptiveApiClient.MakeStudentCallAsync(query);

                var report = new ProfileNarrativeKto1ViewModel
                {
                    AssessmentName = student.CurrentTestEvent.TestEventName,
                    Class = student.CurrentTestEvent.District.ChildLocations.First().ChildLocations.First().Name,
                    District = student.CurrentTestEvent.District.Name,
                    Grade = student.CurrentTestEvent.Grade.Name,
                    School = student.CurrentTestEvent.District.ChildLocations.First().Name,
                    StudentFirstName = student.Name.FirstName,
                    StudentId = student.UserId.ToString(),
                    StudentExternalId = student.ExternalId,
                    StudentLastName = student.Name.LastName,
                    SubjectName = student.CurrentTestEvent.SubjectName,
                    TestDate = student.CurrentTestEvent.TestDate.ToShortDateString(),
                    PldName = student.CurrentTestEvent.PldName,
                    PldLevel = student.CurrentTestEvent.PldLevel,
                    GraphqlQuery = ConfigSettings.IsEnvironmentProd ? "" : query
                };

                if (!string.IsNullOrEmpty(student.CurrentTestEvent.PldName))
                    report.PerformanceLevelDescriptor = await GetPerformanceLevelDescriptorKto1(student.CurrentTestEvent.PldName);

                if (student.CurrentTestEvent.PldLevel != null)
                    report.PerformanceLevelStatement = await GetPerformanceLevelStatementKto1(student.CurrentTestEvent.PldName, student.CurrentTestEvent.PldLevel);

                reports.Add(report);
            }

            return reports;
        }

        public async Task<PerformanceLevelDescriptor> GetPerformanceLevelDescriptorKto1(string pldName)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildPerformanceLevelDescriptorKto1QueryString(currentFilters.GetSubject(), pldName);
            var performanceLevelDescriptor = await _adaptiveApiClient.MakePerformanceLevelDescriptorCallAsync(query, currentFilters.GetSubject(), pldName);

            return performanceLevelDescriptor;
        }

        public async Task<PerformanceLevelStatement> GetPerformanceLevelStatementKto1(string pldName, int? pldLevel)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildPerformanceLevelStatementKto1QueryString(currentFilters.GetSubject(), pldName, pldLevel);
            var performanceLevelStatement = await _adaptiveApiClient.MakePerformanceLevelStatementCallAsync(query, currentFilters.GetSubject(), pldName, pldLevel);

            return performanceLevelStatement;
        }

        public async Task<DifferentiatedReportKto1HierarchyViewModel> GetDifferentiatedReportHierarchyKto1Async()
        {
            var testEventAndQuery = await GetDifferentiatedReportTestEventKto1Async();
            return new DifferentiatedReportKto1HierarchyViewModel
            {
                GraphQlQuery = testEventAndQuery.Item2,
                Values = testEventAndQuery.Item1.DifferentiatedReportKto1
            };
        }

        private async Task<(TestEvent, string)> GetDifferentiatedReportTestEventKto1Async()
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildDifferentiatedReportKto1QueryString(currentFilters, _userData.UserId);
            var cacheKey = $"{_userData.UserId}{currentFilters.RootNodes.First().NodeType.First()}{string.Join("", currentFilters.GetFilterByType(FilterType.ParentLocations).Items.Select(i => i.Value).ToList())}{currentFilters.GetSubject()}{currentFilters.GetSelectedValuesStringOf(FilterType.Grade)}";
            var response = await _adaptiveApiClient.MakeDifferentiatedReportCallAsync(query, cacheKey);

            return (response.TestEvents.First(), query);
        }

        public async Task<DifferentiatedReportKto1ViewModel> GetDifferentiatedReportKto1Async(string studentIds)
        {
            var testEventAndQuery = await GetDifferentiatedReportTestEventKto1Async();
            var testEvent = testEventAndQuery.Item1;
            var differentiatedReportHierarchy = testEvent.DifferentiatedReportKto1;

            var firstNonNullRecord = differentiatedReportHierarchy.FirstOrDefault(r => r.DistrictId != null && r.BuildingId != null && r.Grade != null);

            //TODO:  Dmitriy - should probably check if firstNonNullRecord is NULL and somehow handle it nicely???
            if(firstNonNullRecord == null)
                throw new Exception("Differentiated report Kto1 returned all NULL records.");
            
            var result = new DifferentiatedReportKto1ViewModel
            {
                GraphQlQuery = testEventAndQuery.Item2,
                Values = new DifferentiatedReportKto1ReportValues
                {
                    DistrictId = firstNonNullRecord.DistrictId,
                    DistrictName = firstNonNullRecord.DistrictName,
                    Grade = firstNonNullRecord.Grade,
                    Subject = firstNonNullRecord.Subject,
                    TestEventName = testEvent.Name,
                    TestEventDate = testEvent.Date,
                    Buildings = new List<DifferentiatedReportKto1PldBuilding>()
                }
            };

            var studentIdsList = studentIds.Split(',');

            foreach (var record in differentiatedReportHierarchy)
            {
                if (record.BuildingId == null)
                    continue;

                var building = new DifferentiatedReportKto1PldBuilding
                {
                    BuildingId = record.BuildingId,
                    BuildingName = record.BuildingName,
                    PldStages = new List<DifferentiatedReportKto1PldStage>()
                };

                for (int stageCount = 1; stageCount < 6; ++stageCount)
                {
                    var filteredStudentList = new List<DifferentiatedReportKto1Student>();

                    if (!record.StudentList.Any())
                        continue;

                    //foreach (var studentId in studentIdsList)
                    foreach (var student in record.StudentList)
                    {
                        if (!studentIdsList.Contains(student.StudentId.ToString()))
                            continue;

                        //  var student = record.StudentList.FirstOrDefault(s => s.StudentId == Convert.ToInt32(studentId));
                        filteredStudentList.Add(student);
                    }

                    if (!filteredStudentList.Any())
                        continue;

                    if (filteredStudentList.Any(s => s.PldStageNum == stageCount))
                    {
                        var pldStageName = filteredStudentList.First(s => s.PldStageNum == stageCount).PldStage;
                        var pldStageDescriptor = await GetPerformanceLevelDescriptorKto1(pldStageName);

                        var pldStage = new DifferentiatedReportKto1PldStage
                        {
                            PldStageNum = stageCount,
                            PldStageName = pldStageName,
                            PldStageDescriptorText = pldStageDescriptor.PldDesc,
                            PldLevels = new List<DifferentiatedReportKto1PldLevel>()
                        };

                        for (int levelCount = 1; levelCount < 4; levelCount++)
                        {
                            //TODO:  Make this better
                            if (pldStageName.ToLower() == "pre-emerging" && levelCount == 3 ||
                                pldStageName.ToLower() == "transitioning" && levelCount == 3 ||
                                pldStageName.ToLower() == "independent" && (levelCount == 2 || levelCount == 3))
                                continue;


                            var pldLevelStatement = await GetPerformanceLevelStatementKto1(pldStageName, levelCount);

                            if (filteredStudentList.Any(s => s.PldStageNum == stageCount && s.PldLevel == levelCount))
                            {
                                var pldLevel = new DifferentiatedReportKto1PldLevel
                                {
                                    PldLevelNum = levelCount,
                                    PldLevelName = $"Level {levelCount}",
                                    CanStatement = pldLevelStatement.CanStatement,
                                    NeedPracticeStatement = pldLevelStatement.PracticeStatement,
                                    ReadyStatement = pldLevelStatement.ReadyStatement,
                                    CanDescriptor = pldLevelStatement.CanDescription,
                                    NeedPracticeDescriptor = pldLevelStatement.NeedDescription,
                                    ReadyDescriptor = pldLevelStatement.ReadyDescription,
                                    Classes = new List<DifferentiatedReportKto1PldClass>()
                                };

                                foreach (var student in filteredStudentList)
                                {
                                    if (student.PldStageNum == stageCount && student.PldLevel == levelCount)
                                    {
                                        var currentClass = pldLevel.Classes.FirstOrDefault(c => c.ClassId == student.ClassId.ToString());

                                        if (currentClass == null)
                                        {
                                            currentClass = new DifferentiatedReportKto1PldClass
                                            {
                                                ClassId = student.ClassId != 0 ? student.ClassId.ToString() : record.ClassId.ToString(),
                                                ClassName = student.ClassName ?? record.ClassName,
                                                StudentNames = new List<string>()
                                            };

                                            pldLevel.Classes.Add(currentClass);
                                        }

                                        currentClass.StudentNames.Add(student.StudentName);
                                    }
                                }

                                pldStage.PldLevels.Add(pldLevel);
                            }

                        }

                        building.PldStages.Add(pldStage);
                    }
                }

                result.Values.Buildings.Add(building);
            }

            return result;
        }

        public async Task<CogatRosterModel> GetCogatLocationRosterAsync(string appPath, int? performanceBand, int? domainId, int? domainLevel, int? cogatAbility, string cogatScore)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildCogatRosterQueryString(currentFilters, performanceBand, domainId, domainLevel, cogatAbility, cogatScore, null, false, _userData.UserId);
            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var rosterLevel = ((LocationsFilter)currentFilters.GetFilterByType(FilterType.ChildLocations)).LocationNodeType;

            var model = new CogatRosterModel($"{rosterLevel.FirstCharToUpper()} Comparison", $"{rosterLevel.FirstCharToUpper()} Comparison")
            {
                GraphQlQuery = query,
                RosterType = "compare",
                RosterLevel = rosterLevel
            };

            foreach (var record in user.TestEvents.First().CogatRoster.Records)
            {
                model.Values.Add(new CogatRosterValue
                {
                    NodeId = record.Id,
                    NodeName = record.Name,
                    Npr = record.Npr,
                    Ss = record.TestScore,
                    Verbal = record.Verbal,
                    Quantitative = record.Quantitative,
                    NonVerbal = record.NonVerbal,
                    CompVQ = record.CompVQ,
                    CompVN = record.CompVN,
                    CompQN = record.CompQN,
                    CompVQN = record.CompVQN,
                    Link = $"{appPath}/api/Dashboard/DrillDownLocations?id={record.Id}&name={record.Name}&type={rosterLevel}"
                });
            }

            return model;
        }

        public async Task<CogatRosterModel> GetCogatStudentRosterAsync(string appPath, int? performanceBand, int? domainId, int? domainLevel, int? cogatAbility, string cogatScore, string contentName)
        {
            var currentFilters = (IowaFlexFilterPanel)_sessionManager.Retrieve(SessionKey.IowaFlexFilters);

            var query = _graphQlQueryStringBuilder.BuildCogatRosterQueryString(currentFilters, performanceBand, domainId, domainLevel, cogatAbility, cogatScore, contentName, true, _userData.UserId);
            var user = await _adaptiveApiClient.MakeUserCallAsync(query);

            //TODO:  Error handling

            var model = new CogatRosterModel("Student Name", "Student Name")
            {
                GraphQlQuery = query,
                RosterType = "students",
                RosterLevel = "students"
            };

            foreach (var record in user.TestEvents.First().CogatRoster.Records)
            {
                model.Values.Add(new CogatRosterValue
                {
                    NodeId = record.Id,
                    NodeName = record.Name,
                    Npr = record.Npr,
                    Ss = record.TestScore,
                    Verbal = record.Verbal,
                    Quantitative = record.Quantitative,
                    NonVerbal = record.NonVerbal,
                    CompVQ = record.CompVQ,
                    CompVN = record.CompVN,
                    CompQN = record.CompQN,
                    CompVQN = record.CompVQN,
                    Link = "#"
                });
            }

            return model;
        }

        private string PerformanceLevelText(List<PerformanceLevel> performanceLevels, int performanceId)
        {
            foreach (var performanceLevel in performanceLevels)
                if (performanceLevel.Id == performanceId)
                    return performanceLevel.Text;

            return "";
        }

        private dynamic BuildRoster(string appPath, IowaFlexFilterPanel currentData, User apiResponse, string query)
        {
            dynamic result = new ExpandoObject();
            result.graph_ql_query = ConfigSettings.IsEnvironmentProd ? "" : query;

            var nodesList = apiResponse.TestEvents.First().LocationRoster.Locations;

            if (nodesList == null || !nodesList.Any())
                return new { nodata = true };

            result.roster_type = "compare";
            var rosterLevel = ((LocationsFilter)currentData.GetFilterByType(FilterType.ChildLocations)).LocationNodeType;
            result.roster_level = rosterLevel;

            var columns = new List<dynamic>();

            dynamic nodeHeading = new ExpandoObject();
            nodeHeading.title = $"{rosterLevel.FirstCharToUpper()} Comparison";
            nodeHeading.title_full = $"{rosterLevel.FirstCharToUpper()} Comparison";
            nodeHeading.multi = 0;
            nodeHeading.field = "node_name";
            columns.Add(nodeHeading);

            dynamic ssHeading = new ExpandoObject();
            ssHeading.title = "SS";
            ssHeading.title_full = "Standard Score";
            ssHeading.multi = 0;
            ssHeading.field = "SS";
            columns.Add(ssHeading);

            dynamic nprHeading = new ExpandoObject();
            nprHeading.title = "NPR";
            nprHeading.title_full = "National Percentile Rank";
            nprHeading.multi = 0;
            nprHeading.field = "NPR";
            columns.Add(nprHeading);

            result.columns = columns;

            var values = new List<dynamic>();

            var maxDomains = nodesList.Max(n => n.DomainScores.Count);
            var domainScores = nodesList.First(n => n.DomainScores.Count == maxDomains).DomainScores;

            foreach (var domainScore in domainScores)
            {
                dynamic headCell = new ExpandoObject();
                headCell.title = domainScore.Name;
                headCell.title_full = domainScore.Description;
                headCell.multi = 1;

                var nums = new List<string>();
                var percents = new List<string>();
                foreach (var performanceLevel in domainScore.PerformanceLevels)
                {
                    nums.Add($"DOM_{domainScore.Id}_num_{performanceLevel.Id}");
                    percents.Add($"DOM_{domainScore.Id}_per_{performanceLevel.Id}");
                }
                headCell.fields_num = nums;
                headCell.fields_per = percents;

                columns.Add(headCell);
            }

            foreach (var location in nodesList)
            {
                dynamic row = new ExpandoObject();
                row.node_name = location.Name;
                row.node_id = location.Id;
                row.node_type = location.Id;
                row.link = $"{appPath}/api/Dashboard/DrillDownLocations?id={location.Id}&name={location.Name}&type={rosterLevel}";

                row.SS = location.AverageScore;
                row.NPR = location.NprAverageScore;

                foreach (var domainScore in domainScores)
                {
                    var domain = location.DomainScores.FirstOrDefault(ds => ds.Name == domainScore.Name);

                    if (domain == null)
                        foreach (var performanceLevel in domainScore.PerformanceLevels)
                        {
                            Functions.AddExpandoProperty(row, $"DOM_{domainScore.Id}_num_{performanceLevel.Id}", 0);
                            Functions.AddExpandoProperty(row, $"DOM_{domainScore.Id}_per_{performanceLevel.Id}", 0);
                        }
                    else
                        foreach (var performanceLevel in domain.PerformanceLevels)
                        {
                            Functions.AddExpandoProperty(row, $"DOM_{domainScore.Id}_num_{performanceLevel.Id}", performanceLevel.NumberOfStudents);
                            Functions.AddExpandoProperty(row, $"DOM_{domainScore.Id}_per_{performanceLevel.Id}", performanceLevel.Percent);

                        }
                }

                values.Add(row);
            }

            result.values = values;

            return result;
        }

        private dynamic BuildStudentRoster(User apiResponse, string query)
        {
            dynamic result = new ExpandoObject();

            result.graph_ql_query = ConfigSettings.IsEnvironmentProd ? "" : query;

            var studentsList = apiResponse.TestEvents.First().StudentRoster.Students;

            if (studentsList == null || !studentsList.Any())
                return new { nodata = true };

            result.roster_type = "students";
            result.roster_level = "students";

            var columns = new List<dynamic>();

            dynamic nodeHeading = new ExpandoObject();
            nodeHeading.title = "Student Name";
            nodeHeading.title_full = "Student Name";
            nodeHeading.multi = 0;
            nodeHeading.field = "node_name";
            columns.Add(nodeHeading);

            dynamic ssHeading = new ExpandoObject();
            ssHeading.title = "SS";
            ssHeading.title_full = "Standard Score";
            ssHeading.multi = 0;
            ssHeading.field = "SS";
            columns.Add(ssHeading);

            dynamic nprHeading = new ExpandoObject();
            nprHeading.title = "NPR";
            nprHeading.title_full = "National Percentile Rank";
            nprHeading.multi = 0;
            nprHeading.field = "NPR";
            columns.Add(nprHeading);

            var maxDomains = studentsList.Max(s => s.DomainScores.Count);
            var domainScores = studentsList.First(s => s.DomainScores.Count == maxDomains).DomainScores;
            var domainIds = domainScores.Select(ds => ds.Id).ToList();

            foreach (var domainScore in domainScores)
            {
                dynamic headCell = new ExpandoObject();
                headCell.title = domainScore.Name;
                headCell.title_full = domainScore.Description;
                headCell.multi = 1;

                var fields = new List<string>{
                    $"DOM_{domainScore.Id}_score"
                };

                headCell.fields = fields;

                columns.Add(headCell);
            }

            result.columns = columns;

            var values = new List<dynamic>();

            foreach (var student in studentsList)
            {
                dynamic row = new ExpandoObject();
                row.node_name = $"{student.Name.LastName}, {student.Name.FirstName}";
                row.node_id = student.Id;
                row.externalId = student.ExternalId;
                row.node_type = "STUDENT";
                row.link = "#";

                row.SS = student.TestScore;
                row.NPR = student.Npr;

                foreach (var domainId in domainIds)
                {
                    var performanceLevelId = "*";

                    var domain = student.DomainScores.FirstOrDefault(ds => ds.Id == domainId);
                    if (domain != null)
                    {
                        var performanceLevel = domain.PerformanceLevels.FirstOrDefault(pl => pl.NumberOfStudents == 1);
                        performanceLevelId = performanceLevel?.Id.ToString() ?? "0";
                    }

                    Functions.AddExpandoProperty(row, $"DOM_{domainId}_score", performanceLevelId);
                }

                values.Add(row);
            }

            result.values = values;

            return result;
        }
    }
}