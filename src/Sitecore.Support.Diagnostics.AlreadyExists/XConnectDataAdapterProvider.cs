namespace Sitecore.Support.Analytics.XConnect.DataAccess
{
  using Sitecore.Analytics.DataAccess;
  using Sitecore.Analytics.Model.Entities;
  using Sitecore.Analytics.Processing;
  using Sitecore.Analytics.XConnect.Facets;
  using Sitecore.Data;
  using Sitecore.Diagnostics;
  using Sitecore.Framework.Conditions;
  using Sitecore.XConnect;
  using Sitecore.XConnect.Client;
  using Sitecore.XConnect.Collection.Model;
  using Sitecore.XConnect.Operations;
  using Sitecore.Xml;
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.Linq;
  using System.Reflection;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Xml;
  public class XConnectDataAdapterProvider : Sitecore.Analytics.XConnect.DataAccess.XConnectDataAdapterProvider
  {
    private string[] _facetsToLoad = Array.Empty<string>();

    internal IXdbContextFactory ContextFactory
    {
      get
      {
        return typeof(Sitecore.Analytics.XConnect.DataAccess.XConnectDataAdapterProvider).GetField("ContextFactory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField).GetValue(this) as IXdbContextFactory;
      }
    }

    protected new void AddFacet(XmlNode configNode)
    {
      ValidatorExtensions.IsEqualTo<string>(Condition.Requires<string>(configNode.Name, "configNode.Name"), "facet");
      string attribute = XmlUtil.GetAttribute("facetKey", configNode);
      this.AddFacet(attribute);
      base.AddFacet(configNode);
    }

    internal void AddFacet(string facetKey)
    {
      HashSet<string> hashSet = new HashSet<string>(this._facetsToLoad);
      bool flag = hashSet.Add(facetKey);
      if (flag)
      {
        string[] value = hashSet.ToArray<string>();
        Interlocked.Exchange<string[]>(ref this._facetsToLoad, value);
      }
    }

    private Contact GetContactByTrackerContactId(ID contactId)
    {
      ValidatorExtensions.IsNotNull<ID>(Condition.Requires<ID>(contactId, "contactId"));
      return this.ExecuteWithExceptionHandling<Contact>((IXdbContext xdbContext) => this.GetContactByTrackerContactId(xdbContext, contactId));
    }

    private Contact GetContactByTrackerContactId(IXdbContext xdbContext, ID contactId)
    {
      ValidatorExtensions.IsNotNull<IXdbContext>(Condition.Requires<IXdbContext>(xdbContext, "xdbContext"));
      ValidatorExtensions.IsNotNull<ID>(Condition.Requires<ID>(contactId, "contactId"));
      return this.GetContactByIdentifier(xdbContext, "xDB.Tracker", XConnectDataAdapterProvider.ToXConnectIdentifier(contactId.Guid));
    }

    private Contact GetContactByIdentifier(IXdbContext xdbContext, string source, string identifier)
    {
      ValidatorExtensions.IsNotNull<IXdbContext>(Condition.Requires<IXdbContext>(xdbContext, "xdbContext"));
      ValidatorExtensions.IsNotNull<string>(Condition.Requires<string>(source, "source"));
      ValidatorExtensions.IsNotNull<string>(Condition.Requires<string>(identifier, "identifier"));
      ExpandOptions expandOptions = new ExpandOptions(this._facetsToLoad);
      Contact contact = xdbContext.Get(new IdentifiedContactReference(source, identifier), expandOptions, base.GetOperationTimeout);
      MergeInfo mergeInfo = (contact != null) ? contact.MergeInfo() : null;
      Guid? guid = (mergeInfo != null && mergeInfo.Obsolete) ? new Guid?(mergeInfo.SuccessorContactId) : null;
      bool hasValue = guid.HasValue;
      Contact result;
      if (hasValue)
      {
        result = xdbContext.Get(new ContactReference(guid.Value), expandOptions, base.GetOperationTimeout);
      }
      else
      {
        result = contact;
      }
      return result;
    }

    public override bool SaveContact(IContact contact, ContactSaveOptions contactSaveOptions)
    {
      ValidatorExtensions.IsNotNull<IContact>(Condition.Requires<IContact>(contact, "contact"));
      ValidatorExtensions.IsNotNull<ContactSaveOptions>(Condition.Requires<ContactSaveOptions>(contactSaveOptions, "contactSaveOptions"));
      string contactId = contact.Id.Guid.ToString().ToLower().Replace("-", ""); ;
      Log.Info("[Sitecore.Support.Diagnostics]: Saving contact " + contactId, this);
      System.Diagnostics.StackTrace t = new System.Diagnostics.StackTrace();
      Log.Info("[Sitecore.Support.Diagnostics]: Stack trace - " + t.ToString(), this);
      return this.ExecuteWithExceptionHandling<bool>(delegate (IXdbContext xdbContext)
      {
        bool? isNew = contactSaveOptions.IsNew;
        bool flag = !isNew.HasValue;
        if (flag)
        {
          isNew = new bool?(this.GetContactByTrackerContactId(contact.Id) == null);
        }
        bool value = isNew.Value;
        bool flag6;
        bool result;
        if (value)
        {
          Log.Info("[Sitecore.Support.Diagnostics]: Contact isn't new ", this);
          Sitecore.XConnect.ContactIdentifier contactIdentifier = new Sitecore.XConnect.ContactIdentifier("xDB.Tracker", XConnectDataAdapterProvider.ToXConnectIdentifier(contact.Id.Guid), ContactIdentifierType.Anonymous);
          Contact contact2 = new Contact(new Sitecore.XConnect.ContactIdentifier[]
                {
                        contactIdentifier
              });
          try
          {
            xdbContext.AddContact(contact2);
            Log.Info("[Sitecore.Support.Diagnostics]: xdbContext.AddContact: added xConnectContact", this);
          }
          catch (EntityOperationException ex)
          {
            Log.Error(ex.Message, this);
          }


          Classification classification = new Classification();
          XConnectDataAdapterProvider.CopySystemData(contact.System, classification);
          xdbContext.SetClassification(contact2, classification);
          xdbContext.Submit();
        }
        else
        {
          Log.Info("[Sitecore.Support.Diagnostics]: Contact isn't new ", this);
          Classification classification = GetXConnectClassificationFacet(contact);
          if (classification != null &&
              classification.ClassificationLevel == contact.System.Classification &&
              classification.OverrideClassificationLevel == contact.System.OverrideClassification)
          {
            return true;
          }

          classification = classification ?? new Classification();
          XConnectDataAdapterProvider.CopySystemData(contact.System, classification);

          CollectionModel.SetClassification(xdbContext, new IdentifiedContactReference("xDB.Tracker", XConnectDataAdapterProvider.ToXConnectIdentifier(contact.Id.Guid)), classification);
          xdbContext.Submit();
        } 
        return true;
      });
    }

    private T ExecuteWithExceptionHandling<T>(Func<IXdbContext, T> func)
    {
      T result;
      try
      {
        using (IXdbContext xdbContext = this.ContextFactory.Create())
        {
          result = func(xdbContext);
        }
      }
      catch (Sitecore.Analytics.DataAccess.XdbUnavailableException innerException)
      {
        throw new Sitecore.Analytics.DataAccess.XdbUnavailableException(innerException);
      }
      return result;
    }

    public static void CopySystemData(IContactSystemInfo systemInfoEntity, Classification classification)
    {
      ValidatorExtensions.IsNotNull<IContactSystemInfo>(Condition.Requires<IContactSystemInfo>(systemInfoEntity, "systemInfoEntity"));
      ValidatorExtensions.IsNotNull<Classification>(Condition.Requires<Classification>(classification, "classification"));
      classification.ClassificationLevel = systemInfoEntity.Classification;
      classification.OverrideClassificationLevel = systemInfoEntity.OverrideClassification;
    }

    public static string ToXConnectIdentifier(Guid contactId)
    {
      ValidatorExtensions.IsNotNull<Guid>(Condition.Requires<Guid>(contactId, "contactId"));
      return contactId.ToString("N");
    }

    private Classification GetXConnectClassificationFacet(IContact contact)
    {
      bool flag = !contact.Facets.ContainsKey("XConnectFacets");
      Classification result;
      if (flag)
      {
        result = null;
      }
      else
      {
        IXConnectFacets facet = contact.GetFacet<IXConnectFacets>("XConnectFacets");
        bool flag2 = ((facet != null) ? facet.Facets : null) == null;
        if (flag2)
        {
          result = null;
        }
        else
        {
          bool flag3 = !facet.Facets.ContainsKey("Classification");
          if (flag3)
          {
            result = null;
          }
          else
          {
            result = (facet.Facets["Classification"] as Classification);
          }
        }
      }
      return result;
    }

    private Classification GetClassificationFromXConnect(IXdbContext context, IContact contact)
    {
      new List<IEntityReference<Contact>>().Add(new ContactReference(contact.Id.Guid));
      Contact contactByTrackerContactId = this.GetContactByTrackerContactId(contact.Id);
      bool flag = contactByTrackerContactId != null;
      Classification result;
      if (flag)
      {
        result = contactByTrackerContactId.GetFacet<Classification>("Classification");
      }
      else
      {
        result = null;
      }
      return result;
    }
  }
}
