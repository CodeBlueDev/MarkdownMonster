using System;
using CookComputing.XmlRpc;

namespace WebLogAddin.MetaWebLogApi
{
    /// <summary>
    /// Filtering structure for getting comments.
    /// </summary>
    [XmlRpcMissingMapping(MappingAction.Ignore)]
    public struct XmlRpcCommentFilter
    {
        public string status;
        public int post_id;
        public int number;
        public int offset;
    }
}