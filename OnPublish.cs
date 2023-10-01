using System;
using Microsoft.Xrm.Sdk;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk.Query;
using System.Web.Services.Description;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk.Organization;
using System.Net.NetworkInformation;
//using Microsoft.ApplicationInsights;
//using Microsoft.ApplicationInsights.Extensibility;

namespace DavesVersionControl
{
    public class Onpublish : IPlugin
    {
        string FetchSolution = @"<?xml version=""1.0"" encoding=""utf-16""?>
<fetch top=""50"">
  <entity name=""solution"">
    <all-attributes />
    <filter>
       <condition attribute=""uniquename"" operator=""in"">
        {solutionlist}
      </condition>
    </filter>
  </entity>
</fetch>";

        string FetchForms = @"<fetch top='10'>
  <entity name='solutioncomponent'>
    <link-entity name='systemform' to='objectid' from='formid' alias='sf' link-type='inner'>
      <all-attributes />
    </link-entity>
    <filter>
      <condition attribute='solutionid' operator='eq' value='{targetsolution}' />
    </filter>
  </entity>
</fetch>";
        //7965d2e9-2c2f-ee11-bdf4-000d3aa9a09b
        public void Execute(IServiceProvider serviceProvider)
        {

            // Obtain the tracing service from the service provider.
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            tracingService.Trace("DavesVersionControl Onpublish Plugin execution started.");

            try
            {
                // Obtain the execution context from the service provider.
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService _service = serviceFactory.CreateOrganizationService(context.UserId);

                var Envriovars = GetEnvironmentVariables( tracingService,  _service);
                string targetentity = "";

                var solutionlist = "";
                if (Envriovars.ContainsKey("dm_SolutionsToVersionControl"))
                {
                    var solutionscsv = Envriovars["dm_SolutionsToVersionControl"];
                    var split = solutionscsv.Split(',');
                    foreach (var s in split)
                    {
                        solutionlist += $"<value>{s}</value>";
                    }
                    FetchSolution = FetchSolution.Replace("{solutionlist}", solutionlist);
                }



                // Log all input parameters.
                foreach (string paramName in context.InputParameters.Keys)
                {
                    object paramValue = context.InputParameters[paramName];
                    tracingService.Trace($"Input Parameter: {paramName} = {paramValue}");
                    if (paramValue.GetType() == typeof(string))
                    {
                        var paraString = (string)paramValue;
                        if (paraString.Contains("<entity>"))
                        {
                            int start = paraString.IndexOf("<entity>") + ("<entity>".Length);
                            tracingService.Trace($" start ={start} ");
                            int end = paraString.IndexOf("/");
                            tracingService.Trace($" start ={start} end = {end} string ={paraString}");
                            paraString = paraString.Substring(start);

                            end = paraString.IndexOf("/");
                            tracingService.Trace($" end = {end} string ={paraString}");
                            paraString = paraString.Substring(0, end - 1);
                            tracingService.Trace($"{paraString}");
                            targetentity = paraString;

                        }
                    }
                }
                /*
                // Log pre-image attributes.
                if (context.PreEntityImages.Contains("Target"))
                {
                    Entity preImage = context.PreEntityImages["Target"];
                    foreach (var attribute in preImage.Attributes)
                    {
                        tracingService.Trace($"Pre-Image Attribute: {attribute.Key} = {attribute.Value}");
                    }
                }

                // Log post-image attributes.
                if (context.PostEntityImages.Contains("Target"))
                {
                    Entity postImage = context.PostEntityImages["Target"];
                    foreach (var attribute in postImage.Attributes)
                    {
                        tracingService.Trace($"Post-Image Attribute: {attribute.Key} = {attribute.Value}");
                    }
                }
                */

                var solutioncollection = FetchXML(_service, tracingService, FetchSolution);
                
                var targetsolution = Guid.Empty;
                foreach (Entity sol in solutioncollection.Entities)
                {
                    tracingService.Trace($" solution : {sol.Id.ToString("B")} ");
                    targetsolution = sol.Id;


                    var fetchxml = FetchForms.Replace("{targetsolution}", targetsolution.ToString("B"));

                    var forms = FetchXML(_service, tracingService, fetchxml);

                    tracingService.Trace("Plugin execution completed.");

                    foreach (Entity e in forms.Entities)
                    {
                        tracingService.Trace($" Form  : {e.Id} - {e.Id.ToString("B")} ");
                        try
                        {
                            string jsonString = "";
                            foreach (var kv in e.Attributes)
                            {
                                tracingService.Trace($" attrubte name : {kv.Key}");
                            }

                            string currententitytype = GetAliasedAttributeValue<String>(tracingService, e, "sf.objecttypecode");
                            if (currententitytype == targetentity)
                            {
                                var versionentry = new Entity("dm_versionhistory");

                                var oldrow = RetrieveMostRecentByField(_service, tracingService,
                                    "dm_versionhistory", "dm_formid",
                                    GetAliasedAttributeValue<Guid>(tracingService, e, "sf.formid").ToString()
                                    , new ColumnSet(true));
                                string changes = "";
                                try
                                {
                                    versionentry["dm_entity"] = e.LogicalName;
                                    //versionentry[""] = e.Attributes[""];
                                    versionentry["dm_comment"] = "something changed maybe";


                                    versionentry["dm_entity"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.objecttypecode");
                                    versionentry["dm_formid"] = GetAliasedAttributeValue<Guid>(tracingService, e, "sf.formid").ToString("B");
                                    versionentry["dm_formidunique"] = GetAliasedAttributeValue<Guid>(tracingService, e, "sf.formidunique").ToString("B");
                                    versionentry["dm_formname"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.name");
                                    versionentry["dm_name"] = $"{versionentry["dm_entity"]}:{versionentry["dm_formname"]}:{DateTime.UtcNow.ToString("yyyy/MM/dd hh:mm:ss.fffff")}";
                                    // versionentry["dm_postdata"] = e.Attributes[""];
                                    if (oldrow != null)
                                    {
                                        versionentry["dm_predata"] = oldrow.ToEntityReference();
                                    }
                                    versionentry["dm_savedon"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.publishedon");
                                    versionentry["dm_solutionid"] = e.GetAttributeValue<EntityReference>("solutionid").Id.ToString();
                                    versionentry["dm_solutionname"] = e.GetAttributeValue<EntityReference>("solutionid").Id.ToString();
                                    versionentry["dm_step"] = context.Stage.ToString();
                                    //versionentry["dm_temp"] = e.Attributes["sf.formjson"];
                                    versionentry["dm_componenttype"] = e.GetAttributeValue<OptionSetValue>("componenttype").Value.ToString();
                                    versionentry["dm_componentid"] = e.GetAttributeValue<Guid>("solutioncomponentid").ToString("B");
                                    

                                    versionentry["dm_data"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.formxml");
                                    versionentry["dm_jsondata"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.formjson");



                                    try
                                    {
                                        if (oldrow != null)
                                        {
                                            if (oldrow.Attributes.ContainsKey("dm_data"))
                                            {
                                                if (!String.IsNullOrEmpty((String)oldrow["dm_data"]))
                                                {
                                                    var xmldiff = new XMLDiffrence();
                                                    var thediffrenceXdoc = xmldiff.FindXmlDifferences(tracingService, (String)oldrow["dm_data"], (String)versionentry["dm_data"]);
                                                    if (thediffrenceXdoc != null)
                                                        changes = thediffrenceXdoc.ToString();
                                                    else
                                                    {
                                                        tracingService.Trace("FindXmlDifferences returned null");
                                                    }
                                                }
                                                else
                                                {
                                                    tracingService.Trace($"old record dm_data was empty");
                                                }
                                            }
                                            else
                                            {
                                                tracingService.Trace($"old record didn't have a dm_data ");
                                            }
                                        }
                                        else
                                        {
                                            tracingService.Trace($"no old record");
                                        }

                                    }
                                    catch (Exception ex)
                                    {
                                        changes = $"error in geting the diffrence {ex.Message}";

                                    }
                                    tracingService.Trace($"{changes}");
                                    versionentry["dm_comment"] = changes;

                                    e.Attributes["sf.formxml"] = "removed";
                                    e.Attributes["sf.formjson"] = "removed";

                                    string entityjsonString = JsonSerializer.Serialize(e);
                                    versionentry["dm_temp"] = entityjsonString;


                                }
                                catch (Exception ex)
                                {
                                    tracingService.Trace(ex.Message);
                                    jsonString = JsonSerializer.Serialize(versionentry);
                                    // Display the JSON string
                                    tracingService.Trace(jsonString);
                                    tracingService.Trace(ex.Message);
                                }

                                // jsonString = JsonSerializer.Serialize(versionentry);
                                // Display the JSON string
                                // tracingService.Trace(jsonString);

                                if (changes == "<Differences />")
                                {
                                    tracingService.Trace("No changes detected : not writing the record");
                                }
                                else
                                {
                                    tracingService.Trace($"Changes detected");
                                    var result = _service.Create(versionentry);
                                    tracingService.Trace($"Changes detected id={result}");
                                }
                            }
                            else
                            {
                                tracingService.Trace($" wasn't the right entity type it was '{currententitytype}' we were looking for '{targetentity}'");
                            }
                            /*
                                 <attribute name="ancestorformid" />
        <attribute name="canbedeleted" />
        <attribute name="componentstate" />
        <attribute name="description" />
        <attribute name="formactivationstate" />
        <attribute name="formid" />
        <attribute name="formidunique" />
        <attribute name="formjson" />
        <attribute name="formpresentation" />
        <attribute name="formxml" />
        <attribute name="formxmlmanaged" />
        <attribute name="introducedversion" />
        <attribute name="isairmerged" />
        <attribute name="iscustomizable" />
        <attribute name="isdefault" />
        <attribute name="isdesktopenabled" />
        <attribute name="ismanaged" />
        <attribute name="istabletenabled" />
        <attribute name="name" />
        <attribute name="objecttypecode" />
        <attribute name="organizationid" />
        <attribute name="overwritetime" />
        <attribute name="publishedon" />
        <attribute name="solutionid" />
        <attribute name="supportingsolutionid" />
        <attribute name="type" />
        <attribute name="uniquename" />
        <attribute name="version" />
        <attribute name="versionnumber" />
        <order attribute="publishedon" descending="true" />
                              */


                        }
                        catch (Exception ex)
                        {
                            tracingService.Trace(ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any exceptions to the trace.
                tracingService.Trace($"Error: {ex.Message}");
                throw;
            }
        }

        private EntityCollection FetchXML(IOrganizationService _service, ITracingService tracingService, string fetchXml)
        {
            tracingService.Trace($"fetxml to run :{fetchXml}");
            // Create a FetchExpression object.

            var fetchExpression = new FetchExpression(fetchXml);

            try
            {
                // Execute the FetchXML query.
                EntityCollection results = _service.RetrieveMultiple(fetchExpression);

                return results;
            }
            catch (Exception ex)
            {
                // Handle any exceptions.
                throw new Exception($"Error fetching records: {ex.Message}  - {fetchXml}", ex);
            }
        }
        public Entity RetrieveByField(IOrganizationService _service, ITracingService tracingService, string entityLogicalName, string fieldName, object fieldValue, ColumnSet columnSet)
        {
            try
            {
                // Create a query expression to filter records by the specified field value.
                QueryExpression query = new QueryExpression(entityLogicalName)
                {
                    ColumnSet = columnSet
                };

                query.Criteria.AddCondition(fieldName, ConditionOperator.Equal, fieldValue);

                // Execute the query.
                EntityCollection results = _service.RetrieveMultiple(query);

                // Check if a record was found.
                if (results.Entities.Count > 0)
                {
                    return results.Entities[0]; // Return the first matching record.
                }
                else
                {
                    Console.WriteLine("No records found with the specified field value.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        public Entity RetrieveMostRecentByField(IOrganizationService _service, ITracingService tracingService, string entityLogicalName, string fieldName, object fieldValue, ColumnSet columnSet)
        {
            try
            {
                // Create a query expression to filter and order records.
                QueryExpression query = new QueryExpression(entityLogicalName)
                {
                    ColumnSet = columnSet,
                    Criteria =
                {
                    Filters =
                    {
                        new FilterExpression(LogicalOperator.And)
                        {
                            Conditions =
                            {
                                // Add your condition to filter by the desired field.
                                new ConditionExpression(fieldName, ConditionOperator.Equal,fieldValue)
                            }
                        }
                    }
                }
                    // Order the records in descending order based on the field.

                };
                query.AddOrder("createdon", OrderType.Descending);

                // Execute the query.
                EntityCollection results = _service.RetrieveMultiple(query);

                // Check if any records were found.
                if (results.Entities.Count > 0)
                {
                    return results.Entities[0]; // Return the most recent record.
                }
                else
                {
                    Console.WriteLine("No records found with the specified field value.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }
        public static T GetAliasedAttributeValue<T>(ITracingService tracingService, Entity entity, string attributeName)
        {
            tracingService.Trace($"GetAliasedAttributeValue {attributeName}");
            if (entity == null)
                return default(T);

            AliasedValue fieldAliasValue = entity.GetAttributeValue<AliasedValue>(attributeName);

            if (fieldAliasValue == null)
                return default(T);

            if (fieldAliasValue.Value != null && fieldAliasValue.Value.GetType() == typeof(T))
            {
                var logmessage = $"{(T)fieldAliasValue.Value}";
                if (logmessage.Length > 100) logmessage = $"{logmessage.Substring(0, 90)}   <TRUNCATED org length {logmessage.Length}>";
                tracingService.Trace($"GetAliasedAttributeValue {attributeName} equals {logmessage}");
                return (T)fieldAliasValue.Value;
            }

            return default(T);
        }

        static IReadOnlyDictionary<string, string> EnvVariables;
        static readonly object Lock = new object();

        protected static IReadOnlyDictionary<string, string> GetEnvironmentVariables(ITracingService tracingService, IOrganizationService _service)
        {
            tracingService.Trace($"Entered GetEnvironmentVariables");

            // Singleton pattern to load environment variables less
            if (EnvVariables == null)
            {
                lock (Lock)
                {
                    if (EnvVariables == null)
                    {
                        tracingService.Trace($"Load environment variables");
                        var envVariables = new Dictionary<string, string>();

                        var query = new QueryExpression("environmentvariabledefinition")
                        {
                            ColumnSet = new ColumnSet("statecode", "defaultvalue", "valueschema",
                              "schemaname", "environmentvariabledefinitionid", "type"),
                            LinkEntities =
                        {
                            new LinkEntity
                            {
                                JoinOperator = JoinOperator.LeftOuter,
                                LinkFromEntityName = "environmentvariabledefinition",
                                LinkFromAttributeName = "environmentvariabledefinitionid",
                                LinkToEntityName = "environmentvariablevalue",
                                LinkToAttributeName = "environmentvariabledefinitionid",
                                Columns = new ColumnSet("statecode", "value", "environmentvariablevalueid"),
                                EntityAlias = "v"
                            }
                        }
                        };

                        var results = _service.RetrieveMultiple(query);
                        if (results?.Entities.Count > 0)
                        {
                            foreach (var entity in results.Entities)
                            {
                                var schemaName = entity.GetAttributeValue<string>("schemaname");
                                var value = entity.GetAttributeValue<AliasedValue>("v.value")?.Value?.ToString();
                                var defaultValue = entity.GetAttributeValue<string>("defaultvalue");

                                tracingService.Trace($"- schemaName:{schemaName}, value:{value}, defaultValue:{defaultValue}");
                                if (schemaName != null && !envVariables.ContainsKey(schemaName))
                                    envVariables.Add(schemaName, string.IsNullOrEmpty(value) ? defaultValue : value);
                            }
                        }

                        EnvVariables = envVariables;
                    }
                }
            }

            tracingService.Trace($"Exiting GetEnvironmentVariables");
            return EnvVariables;
        }
    }



}