using CookComputing.XmlRpc;

namespace WebLogAddin.MetaWebLogApi
{
    /// <summary>
    /// Term info attached to a blog item.
    /// </summary>
    [XmlRpcMissingMapping(MappingAction.Ignore)]
    public class XmlRpcTerm
    {
        public string taxonomy;
        public string[] terms;
    }
}
