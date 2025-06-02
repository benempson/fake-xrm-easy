using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FakeXrmEasy.Extensions;
using FakeXrmEasy.Extensions.FetchXml;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FakeXrmEasy.FakeMessageExecutors
{
    public class RetrieveMultipleRequestExecutor : IFakeMessageExecutor
    {
        public bool CanExecute(OrganizationRequest request)
        {
            return request is RetrieveMultipleRequest;
        }

        public OrganizationResponse Execute(OrganizationRequest req, XrmFakedContext ctx)
        {
            var request = req as RetrieveMultipleRequest;
            List<Entity> list = null;
            PagingInfo pageInfo = null;
            QueryExpression qe;

            string entityName = null;

            if (request.Query is QueryExpression)
            {
                qe = (request.Query as QueryExpression).Clone();
                entityName = qe.EntityName;

                var linqQuery = XrmFakedContext.TranslateQueryExpressionToLinq(ctx, qe);
                list = linqQuery.ToList();
            }
            else if (request.Query is FetchExpression)
            {
                var fetchXml = (request.Query as FetchExpression).Query;
                var xmlDoc = XrmFakedContext.ParseFetchXml(fetchXml);
                qe = XrmFakedContext.TranslateFetchXmlDocumentToQueryExpression(ctx, xmlDoc);
                entityName = qe.EntityName;

                var linqQuery = XrmFakedContext.TranslateQueryExpressionToLinq(ctx, qe);
                list = linqQuery.ToList();

                if (xmlDoc.IsAggregateFetchXml())
                {
                    list = XrmFakedContext.ProcessAggregateFetchXml(ctx, xmlDoc, list);
                }
            }
            else if (request.Query is QueryByAttribute)
            {
                // We instantiate a QueryExpression to be executed as we have the implementation done already
                var query = request.Query as QueryByAttribute;
                qe = new QueryExpression(query.EntityName);
                entityName = qe.EntityName;

                qe.ColumnSet = query.ColumnSet;
                qe.Criteria = new FilterExpression();
                for (var i = 0; i < query.Attributes.Count; i++)
                {
                    qe.Criteria.AddCondition(new ConditionExpression(query.Attributes[i], ConditionOperator.Equal, query.Values[i]));
                }

                foreach (var order in query.Orders)
                {
                    qe.AddOrder(order.AttributeName, order.OrderType);
                }

                qe.PageInfo = query.PageInfo;
                qe.TopCount = query.TopCount;

                // QueryExpression now done... execute it!
                var linqQuery = XrmFakedContext.TranslateQueryExpressionToLinq(ctx, qe);
                list = linqQuery.ToList();
            }
            else
            {
                throw PullRequestException.NotImplementedOrganizationRequest(request.Query.GetType());
            }

            if (qe.Distinct)
            {
                list = GetDistinctEntities(list);
            }

            // Handle the top count before taking paging into account
            if (qe.TopCount != null && qe.TopCount.Value < list.Count)
            {
                list = list.Take(qe.TopCount.Value).ToList();
            }

            // Handle TotalRecordCount here?
            int totalRecordCount = -1;
            if (qe?.PageInfo?.ReturnTotalRecordCount == true)
            {
                totalRecordCount = list.Count;
            }

            // Handle paging
            var pageSize = ctx.MaxRetrieveCount;
            pageInfo = qe.PageInfo;
            int pageNumber = 1;
            if (pageInfo != null && pageInfo.PageNumber > 0)
            {
                pageNumber = pageInfo.PageNumber;
                pageSize = pageInfo.Count == 0 ? ctx.MaxRetrieveCount : pageInfo.Count;
            }

            // Figure out where in the list we need to start and how many items we need to grab
            int numberToGet = pageSize;
            int startPosition = 0;

            if (pageNumber != 1)
            {
                startPosition = (pageNumber - 1) * pageSize;
            }

            if (list.Count < pageSize)
            {
                numberToGet = list.Count;
            }
            else if (list.Count - pageSize * (pageNumber - 1) < pageSize)
            {
                numberToGet = list.Count - (pageSize * (pageNumber - 1));
            }

            var recordsToReturn = startPosition + numberToGet > list.Count ? new List<Entity>() : list.GetRange(startPosition, numberToGet);

            recordsToReturn.ForEach(e => e.ApplyDateBehaviour(ctx));
            recordsToReturn.ForEach(e => PopulateFormattedValues(e));

            var response = new RetrieveMultipleResponse
            {
                Results = new ParameterCollection
                {
                    { "EntityCollection", new EntityCollection(recordsToReturn) }
                }
            };
            response.EntityCollection.EntityName = entityName;
            response.EntityCollection.MoreRecords = (list.Count - pageSize * pageNumber) > 0;
            response.EntityCollection.TotalRecordCount = totalRecordCount;

            if (response.EntityCollection.MoreRecords)
            {
                var first = response.EntityCollection.Entities.First();
                var last = response.EntityCollection.Entities.Last();
                response.EntityCollection.PagingCookie = $"<cookie page=\"{pageNumber}\"><{first.LogicalName}id last=\"{last.Id.ToString("B").ToUpper()}\" first=\"{first.Id.ToString("B").ToUpper()}\" /></cookie>";
            }

            return response;
        }

        /// <summary>
        /// Populates the formmated values property of this entity record based on the proxy types
        /// </summary>
        /// <param name="e"></param>
        protected void PopulateFormattedValues(Entity e)
        {
            // Iterate through attributes and retrieve formatted values based on type
            foreach (var attKey in e.Attributes.Keys)
            {
                var value = e[attKey];
                string formattedValue = "";
                if (!e.FormattedValues.ContainsKey(attKey) && (value != null))
                {
                    bool bShouldAdd;
                    formattedValue = this.GetFormattedValueForValue(value, out bShouldAdd);
                    if (bShouldAdd)
                    {
                        e.FormattedValues.Add(attKey, formattedValue);
                    }
                }
            }
        }

        protected string GetFormattedValueForValue(object value, out bool bShouldAddFormattedValue)
        {
            bShouldAddFormattedValue = false;
            var sFormattedValue = string.Empty;

            if (value is Enum)
            {
                // Retrieve the enum type
                sFormattedValue = Enum.GetName(value.GetType(), value);
                bShouldAddFormattedValue = true;
            }
            else if (value is AliasedValue)
            {
                return this.GetFormattedValueForValue((value as AliasedValue)?.Value, out bShouldAddFormattedValue);
            }

            return sFormattedValue;
        }

        public Type GetResponsibleRequestType()
        {
            return typeof(RetrieveMultipleRequest);
        }

        private static List<Entity> GetDistinctEntities(IEnumerable<Entity> input)
        {
            var output = new List<Entity>();

            foreach (var entity in input)
            {
                if (output.Count == 0)
                    output.Add(entity);
                else
                {
                    //BE 250514: If there are no entities of the same type, obviously it's not a duplicate!!
                    List<Entity> checkEnts = output.Where(e => e.LogicalName == entity.LogicalName).ToList();
                    if (checkEnts.Count == 0)
                        output.Add(entity);
                    else
                    {
                        bool isDup = false;
                        bool checkAtts = false;

                        if (entity.Id != Guid.Empty)
                        {
                            foreach (var entity2 in checkEnts)
                            {
                                if (entity2.Id == Guid.Empty)
                                    throw new Exception("entity2 has no Id! Can't compare...");

                                if (entity.Id == entity2.Id)
                                {
                                    isDup = true;
                                    break;
                                }
                            }
                        }

                        if (checkAtts)
                        {
                            StringBuilder entityAtts = new StringBuilder();
                            List<KeyValuePair<string, object>> atts = entity.Attributes.OrderBy(kvp => kvp.Key).ToList();

                            foreach (KeyValuePair<string, object> kvp in atts)
                            {
                                if (kvp.Value.GetType() == typeof(AliasedValue))
                                    entityAtts.AppendLine($"{kvp.Key}: {((AliasedValue)kvp.Value).Value}");
                                else if (kvp.Value.GetType() == typeof(Money))
                                    entityAtts.AppendLine($"{kvp.Key}: {((Money)kvp.Value).Value}");
                                else if (kvp.Value.GetType() == typeof(OptionSetValue))
                                    entityAtts.AppendLine($"{kvp.Key}: {((OptionSetValue)kvp.Value).Value}");
                                else if (kvp.Value.GetType() == typeof(EntityReference))
                                    entityAtts.AppendLine($"{kvp.Key}: {((EntityReference)kvp.Value).Id}");
                                else
                                    entityAtts.AppendLine($"{kvp.Key}: {kvp.Value}");
                            }

                            foreach (var entity2 in checkEnts)
                            {
                                StringBuilder entity2Atts = new StringBuilder();
                                List<KeyValuePair<string, object>> atts2 = entity2.Attributes.OrderBy(kvp => kvp.Key).ToList();

                                foreach (KeyValuePair<string, object> kvp in atts2)
                                {
                                    if (kvp.Value.GetType() == typeof(AliasedValue))
                                        entity2Atts.AppendLine($"{kvp.Key}: {((AliasedValue)kvp.Value).Value}");
                                    else if (kvp.Value.GetType() == typeof(Money))
                                        entity2Atts.AppendLine($"{kvp.Key}: {((Money)kvp.Value).Value}");
                                    else if (kvp.Value.GetType() == typeof(OptionSetValue))
                                        entity2Atts.AppendLine($"{kvp.Key}: {((OptionSetValue)kvp.Value).Value}");
                                    else if (kvp.Value.GetType() == typeof(EntityReference))
                                        entity2Atts.AppendLine($"{kvp.Key}: {((EntityReference)kvp.Value).Id}");
                                    else
                                        entity2Atts.AppendLine($"{kvp.Key}: {kvp.Value}");
                                }

                                //BE 250602: If there is a match, then this is a dup entity and we shouldn't check any further
                                if (entityAtts.ToString().Equals(entity2Atts.ToString()))
                                {
                                    isDup = true;
                                    break;
                                }
                            }
                        }

                        //BE 250602: If no dup was detected, then we can add the entity to the output
                        if (!isDup)
                            output.Add(entity);
                    }
                }
                
                //if (!output.Any(i => i.LogicalName == entity.LogicalName && i.Attributes.SequenceEqual(entity.Attributes)))
                //{
                //    output.Add(entity);
                //}
            }

            return output;
        }
    }
}
