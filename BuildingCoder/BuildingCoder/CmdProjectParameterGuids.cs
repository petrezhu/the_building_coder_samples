#region Header
//
// CmdProjectParameterGuids.cs - determine and report all project parameter GUIDs
//
// Copyright (C) 2015-2019 by CoderBoy and Jeremy Tammik, Autodesk Inc. All rights reserved.
//
// Keywords: The Building Coder Revit API C# .NET add-in.
//
// Written by CoderBoy, cf.
// http://forums.autodesk.com/t5/revit-api/reporting-on-project-parameter-definitions-need-guids/m-p/5947552
// http://forums.autodesk.com/t5/revit-api/create-project-parameter-with-quot-values-can-vary-by-group/m-p/5939455
//
#endregion // Header

#region Namespaces
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#endregion // Namespaces

namespace BuildingCoder
{
  [Transaction( TransactionMode.Manual )]
  class CmdProjectParameterGuids : IExternalCommand
  {
    #region Delete non-shared project parameter
    /// <summary>
    /// Return project parameter id for given name
    /// for https://forums.autodesk.com/t5/revit-api-forum/create-view-filters-for-project-parameter/m-p/9051132
    /// </summary>
    ElementId GetProjectParameterId(
      Document doc, 
      string name )
    {
      ParameterElement pElem 
        = new FilteredElementCollector( doc )
          .OfClass( typeof( ParameterElement ) )
          .Cast<ParameterElement>()
          .Where( e => e.Name.Equals(name) )
          .FirstOrDefault();

      return pElem ?.Id;
    }

    /// <summary>
    /// Delete non-shared project parameter by name
    /// for https://forums.autodesk.com/t5/revit-api-forum/deleting-a-non-shared-project-parameter/td-p/5975020
    /// </summary>
    void DeleteNonSharedProjectParam( 
      Document doc, 
      string parametername )
    {
      ParameterElement projectparameter
        = new FilteredElementCollector( doc )
          .WhereElementIsNotElementType()
          .OfClass( typeof( ParameterElement ) )
          .Cast<ParameterElement>()
          .Where( e => e.GetDefinition()
            .Name.Equals( parametername ) )
          .FirstOrDefault();

      if( projectparameter != null )
      {
        doc.Delete( projectparameter.Id );
      }
    }
    #endregion // Delete non-shared project parameter

    #region Data holding class
    /// <summary>
    /// This class contains information discovered 
    /// about a (shared or non-shared) project parameter 
    /// </summary>
    class ProjectParameterData
    {
      public Definition Definition = null;
      public ElementBinding Binding = null;
      public string Name = null;                // Needed because accsessing the Definition later may produce an error.
      public bool IsSharedStatusKnown = false;  // Will probably always be true when the data is gathered
      public bool IsShared = false;
      public string GUID = null;
    }
    #endregion // Data holding class

    #region Private helper methods
    /// <summary>
    /// Returns a list of the objects containing 
    /// references to the project parameter definitions
    /// </summary>
    /// <param name="doc">The project document being quereied</param>
    /// <returns></returns>
    static List<ProjectParameterData>
      GetProjectParameterData(
        Document doc )
    {
      // Following good SOA practices, first validate incoming parameters

      if( doc == null )
      {
        throw new ArgumentNullException( "doc" );
      }

      if( doc.IsFamilyDocument )
      {
        throw new Exception( "doc can not be a family document." );
      }

      List<ProjectParameterData> result
        = new List<ProjectParameterData>();

      BindingMap map = doc.ParameterBindings;
      DefinitionBindingMapIterator it
        = map.ForwardIterator();
      it.Reset();
      while( it.MoveNext() )
      {
        ProjectParameterData newProjectParameterData
          = new ProjectParameterData();

        newProjectParameterData.Definition = it.Key;
        newProjectParameterData.Name = it.Key.Name;
        newProjectParameterData.Binding = it.Current
          as ElementBinding;

        result.Add( newProjectParameterData );
      }
      return result;
    }

    /// <summary>
    /// This method takes a category and information 
    /// about a project parameter and adds a binding 
    /// to the category for the parameter.  It will 
    /// throw an exception if the parameter is already 
    /// bound to the desired category.  It returns
    /// whether or not the API reports that it 
    /// successfully bound the parameter to the 
    /// desired category.
    /// </summary>
    /// <param name="doc">The project document in which the project parameter has been defined</param>
    /// <param name="projectParameterData">Information about the project parameter</param>
    /// <param name="category">The additional category to which to bind the project parameter</param>
    /// <returns></returns>
    static bool AddProjectParameterBinding(
      Document doc,
      ProjectParameterData projectParameterData,
      Category category )
    {
      // Following good SOA practices, first validate incoming parameters

      if( doc == null )
      {
        throw new ArgumentNullException( "doc" );
      }

      if( doc.IsFamilyDocument )
      {
        throw new Exception(
          "doc can not be a family document." );
      }

      if( projectParameterData == null )
      {
        throw new ArgumentNullException(
          "projectParameterData" );
      }

      if( category == null )
      {
        throw new ArgumentNullException( "category" );
      }

      bool result = false;

      CategorySet cats = projectParameterData.Binding
        .Categories;

      if( cats.Contains( category ) )
      {
        // It's already bound to the desired category.  
        // Nothing to do.
        string errorMessage = string.Format(
          "The project parameter '{0}' is already bound to the '{1}' category.",
          projectParameterData.Definition.Name,
          category.Name );

        throw new Exception( errorMessage );
      }

      cats.Insert( category );

      // See if the parameter is an instance or type parameter.

      InstanceBinding instanceBinding
        = projectParameterData.Binding as InstanceBinding;

      if( instanceBinding != null )
      {
        // Is an Instance parameter

        InstanceBinding newInstanceBinding
          = doc.Application.Create
            .NewInstanceBinding( cats );

        if( doc.ParameterBindings.ReInsert(
          projectParameterData.Definition,
          newInstanceBinding ) )
        {
          result = true;
        }
      }
      else
      {
        // Is a type parameter
        TypeBinding typeBinding
          = doc.Application.Create
            .NewTypeBinding( cats );

        if( doc.ParameterBindings.ReInsert(
          projectParameterData.Definition, typeBinding ) )
        {
          result = true;
        }
      }
      return result;
    }

    /// <summary>
    /// This method populates the appropriate values 
    /// on a ProjectParameterData object with 
    /// information from the given Parameter object.
    /// </summary>
    /// <param name="parameter">The Parameter object with source information</param>
    /// <param name="projectParameterDataToFill">The ProjectParameterData object to fill</param>
    static void PopulateProjectParameterData(
      Parameter parameter,
      ProjectParameterData projectParameterDataToFill )
    {
      // Following good SOA practices, validate incoming parameters first.

      if( parameter == null )
      {
        throw new ArgumentNullException( "parameter" );
      }

      if( projectParameterDataToFill == null )
      {
        throw new ArgumentNullException(
          "projectParameterDataToFill" );
      }

      projectParameterDataToFill.IsSharedStatusKnown = true;
      projectParameterDataToFill.IsShared = parameter.IsShared;
      if( parameter.IsShared )
      {
        if( parameter.GUID != null )
        {
          projectParameterDataToFill.GUID = parameter.GUID.ToString();
        }
      }
    }  // end of PopulateProjectParameterData
    #endregion // Private helper methods

    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Document doc = uidoc.Document;

      if( doc.IsFamilyDocument )
      {
        message = "The document must be a project document.";
        return Result.Failed;
      }

      // Get the (singleton) element that is the 
      // ProjectInformation object.  It can only have 
      // instance parameters bound to it, and it is 
      // always guaranteed to exist.

      Element projectInfoElement
        = new FilteredElementCollector( doc )
          .OfCategory( BuiltInCategory.OST_ProjectInformation )
          .FirstElement();

      // Get the first wall type element.  It can only 
      // have type parameters bound to it, and there is 
      // always guaranteed to be at least one of these.

      Element firstWallTypeElement
        = new FilteredElementCollector( doc )
          .OfCategory( BuiltInCategory.OST_Walls )
          .WhereElementIsElementType()
          .FirstElement();

      CategorySet categories = null;
      Parameter foundParameter = null;

      // Get the list of information about all project 
      // parameters, calling our helper method, below.  

      List<ProjectParameterData> projectParametersData
        = GetProjectParameterData( doc );

      // In order to be able to query whether or not a 
      // project parameter is shared or not, and if it 
      // is shared then what it's GUID is, we must ensure 
      // it exists in the Parameters collection of an 
      // element.
      // This is because we cannot query this information 
      // directly from the project parameter bindings 
      // object.
      // So each project parameter will attempt to be 
      // temporarily bound to a known object so a 
      // Parameter object created from it will exist 
      // and can be queried for this additional 
      // information.

      foreach( ProjectParameterData projectParameterData
        in projectParametersData )
      {
        if( projectParameterData.Definition != null )
        {
          categories = projectParameterData.Binding.Categories;
          if( !categories.Contains( projectInfoElement.Category ) )
          {
            // This project parameter is not already 
            // bound to the ProjectInformation category,
            // so we must temporarily bind it so we can 
            // query that object for it.

            using( Transaction tempTransaction
              = new Transaction( doc ) )
            {
              tempTransaction.Start( "Temporary" );

              // Try to bind the project parameter do 
              // the project information category, 
              // calling our helper method, below.

              if( AddProjectParameterBinding(
                doc, projectParameterData,
                projectInfoElement.Category ) )
              {
                // successfully bound
                foundParameter
                  = projectInfoElement.get_Parameter(
                    projectParameterData.Definition );

                if( foundParameter == null )
                {
                  // Must be a shared type parameter, 
                  // which the API reports that it binds
                  // to the project information category 
                  // via the API, but doesn't ACTUALLY 
                  // bind to the project information 
                  // category.  (Sheesh!)

                  // So we must use a different, type 
                  // based object known to exist, and 
                  // try again.

                  if( !categories.Contains(
                    firstWallTypeElement.Category ) )
                  {
                    // Add it to walls category as we 
                    // did with project info for the 
                    // others, calling our helper 
                    // method, below.

                    if( AddProjectParameterBinding(
                      doc, projectParameterData,
                      firstWallTypeElement.Category ) )
                    {
                      // Successfully bound
                      foundParameter
                        = firstWallTypeElement.get_Parameter(
                          projectParameterData.Definition );
                    }
                  }
                  else
                  {
                    // The project parameter was already 
                    // bound to the Walls category.
                    foundParameter
                      = firstWallTypeElement.get_Parameter(
                        projectParameterData.Definition );
                  }

                  if( foundParameter != null )
                  {
                    PopulateProjectParameterData(
                      foundParameter,
                      projectParameterData );
                  }
                  else
                  {
                    // Wouldn't bind to the walls 
                    // category or wasn't found when 
                    // already bound.
                    // This should probably never happen?

                    projectParameterData.IsSharedStatusKnown
                      = false;  // Throw exception?
                  }
                }
                else
                {
                  // Found the correct parameter 
                  // instance on the Project 
                  // Information object, so use it.

                  PopulateProjectParameterData(
                    foundParameter,
                    projectParameterData );
                }
              }
              else
              {
                // The API reports it couldn't bind 
                // the parameter to the ProjectInformation 
                // category.
                // This only happens with non-shared 
                // Project parameters, which have no 
                // GUID anyway.

                projectParameterData.IsShared = false;
                projectParameterData.IsSharedStatusKnown = true;
              }
              tempTransaction.RollBack();
            }
          }
          else
          {
            // The project parameter was already bound 
            // to the Project Information category.

            foundParameter
              = projectInfoElement.get_Parameter(
                projectParameterData.Definition );

            if( foundParameter != null )
            {
              PopulateProjectParameterData(
                foundParameter, projectParameterData );
            }
            else
            {
              // This will probably never happen.

              projectParameterData.IsSharedStatusKnown
                = false;  // Throw exception?
            }
          }

        }  // Whether or not the Definition object could be found

      }  // For each original project parameter definition

      StringBuilder sb = new StringBuilder();

      // Build column headers

      sb.AppendLine( "PARAMETER NAME\tIS SHARED?\tGUID" );

      // Add each row.

      foreach( ProjectParameterData projectParameterData
        in projectParametersData )
      {
        sb.Append( projectParameterData.Name );
        sb.Append( "\t" );

        if( projectParameterData.IsSharedStatusKnown )
        {
          sb.Append( projectParameterData.IsShared.ToString() );
        }
        else
        {
          sb.Append( "<Unknown>" );
        }

        if( projectParameterData.IsSharedStatusKnown &&
            projectParameterData.IsShared )
        {
          sb.Append( "\t" );
          sb.Append( projectParameterData.GUID );
        }
        sb.AppendLine();
      }

      System.Windows.Clipboard.Clear();
      System.Windows.Clipboard.SetText( sb.ToString() );

      TaskDialog resultsDialog = new TaskDialog(
        "Results are in the Clipboard" );

      resultsDialog.MainInstruction
        = "Results are in the Clipboard";

      resultsDialog.MainContent
        = "Paste the clipboard into a spreadsheet "
          + "program to see the results.";

      resultsDialog.Show();

      return Result.Succeeded;
    }
  }
}
