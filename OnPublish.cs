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
//using Microsoft.ApplicationInsights;
//using Microsoft.ApplicationInsights.Extensibility;

namespace DavesVersionControl
{
    public class Onpublish : IPlugin
    {
        string FetchSolution =  $@"<?xml version=""1.0"" encoding=""utf-16""?>
<fetch top=""50"">
  <entity name=""solution"">
    <attribute name=""solutionid"" />
    <attribute name=""configurationpageid"" />
    <attribute name=""createdby"" />
    <attribute name=""createdon"" />
    <attribute name=""createdonbehalfby"" />
    <attribute name=""description"" />
    <attribute name=""friendlyname"" />
    <attribute name=""installedon"" />
    <attribute name=""isapimanaged"" />
    <attribute name=""ismanaged"" />
    <attribute name=""isvisible"" />
    <attribute name=""modifiedby"" />
    <attribute name=""modifiedon"" />
    <attribute name=""modifiedonbehalfby"" />
    <attribute name=""organizationid"" />
    <attribute name=""parentsolutionid"" />
    <attribute name=""pinpointassetid"" />
    <attribute name=""pinpointsolutiondefaultlocale"" />
    <attribute name=""pinpointsolutionid"" />
    <attribute name=""publisherid"" />
    <attribute name=""solutionpackageversion"" />
    <attribute name=""solutiontype"" />
    <attribute name=""templatesuffix"" />
    <attribute name=""uniquename"" />
    <attribute name=""updatedon"" />
    <attribute name=""upgradeinfo"" />
    <attribute name=""version"" />
    <attribute name=""versionnumber"" />
    <filter>
      <condition attribute=""uniquename"" operator=""eq"" value=""demo"" />
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
            tracingService.Trace("Plugin execution started.");

            try
            {
                // Obtain the execution context from the service provider.
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService _service = serviceFactory.CreateOrganizationService(context.UserId);





                // Log all input parameters.
                foreach (string paramName in context.InputParameters.Keys)
                {
                    object paramValue = context.InputParameters[paramName];
                    tracingService.Trace($"Input Parameter: {paramName} = {paramValue}");
                }

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

                var solutioncollection = FetchXML(_service, tracingService ,FetchSolution);
                // Your plugin logic here...
                var targetsolution = Guid.Empty;
                foreach(Entity e in solutioncollection.Entities)
                {
                    tracingService.Trace($" solution : {e.Id} - {e.Id.ToString("B")} ");
                    targetsolution = e.Id;

                }

                var fetchxml = FetchForms.Replace("{targetsolution}", targetsolution.ToString("B"));

                var forms = FetchXML(_service,tracingService, fetchxml);

                tracingService.Trace("Plugin execution completed.");

                foreach (Entity e in forms.Entities)
                {
                    tracingService.Trace($" form  : {e.Id} - {e.Id.ToString("B")} ");
                    try
                    {
                        string jsonString = "";
                        foreach (var kv in e.Attributes)
                        {
                            tracingService.Trace($" attrubte name : {kv.Key}");
                        }

                        var versionentry = new Entity("dm_versionhistory");

                        try
                        {
                            versionentry["dm_entity"] = e.LogicalName;
                            //versionentry[""] = e.Attributes[""];
                            versionentry["dm_comment"] = "something changed maybe";
                            

                            versionentry["dm_entity"] = GetAliasedAttributeValue<String>(tracingService,e,"sf.objecttypecode");
                            //versionentry["dm_formid"] = ((Guid)e.GetAttributeValue<AliasedValue>("sf.formid").Value).ToString();
                            versionentry["dm_formid"] = GetAliasedAttributeValue<Guid>(tracingService, e, "sf.formid").ToString();
                           versionentry["dm_formname"] = GetAliasedAttributeValue<String>(tracingService, e,"sf.name");
                            versionentry["dm_name"] = "asdf";
                            // versionentry["dm_postdata"] = e.Attributes[""];
                            // versionentry["dm_predata"] = e.Attributes[""];
                            versionentry["dm_savedon"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.publishedon");
                            versionentry["dm_solutionid"] = e.GetAttributeValue<EntityReference>("solutionid").Id.ToString();
                            versionentry["dm_solutionname"] = e.GetAttributeValue<EntityReference>("solutionid").Id.ToString();
                            versionentry["dm_step"] = context.Stage.ToString();
                            //versionentry["dm_temp"] = e.Attributes["sf.formjson"];
                            versionentry["dm_componenttype"] = e.GetAttributeValue<OptionSetValue>("componenttype").Value.ToString();
                            versionentry["dm_data"] = GetAliasedAttributeValue<String>(tracingService, e, "sf.formxml");

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
                        tracingService.Trace("timetowrite value");
                        _service.Create(versionentry);
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
            catch (Exception ex)
            {
                // Log any exceptions to the trace.
                tracingService.Trace($"Error: {ex.Message}");
                throw;
            }
        }

        private EntityCollection FetchXML(IOrganizationService _service,ITracingService tracingService,string fetchXml)
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

        public static T GetAliasedAttributeValue<T>(ITracingService tracingService,Entity entity, string attributeName)
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

    }


}