namespace Whitewashing.Console.ServicesImpl2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;

    public interface IIncomingMessage
    {
        string Text { get; }
        Task Reply(OutgoingMessage message);
    }
    public class OutgoingMessage
    {
        public string Text { get; set; }
    }
    public interface IAuditableEntity
    {
        IReadOnlyCollection<string> TextFragments => Array.Empty<string>();
        string CanonicalLink { get; set; }
    }
    public interface IAuditableRelation : IAuditableEntity
    {
    }

    public interface IChatActor
    {
        Task Process(IIncomingMessage message);
    }

    //singleton
    public interface ChatConnectionHolder
    {
        Task SendMessage(OutgoingMessage message);
    }

    public interface IEntityAuditor
    {
        Task<(bool Hit, string Decription)> Audit(IAuditableRelation value);
        Task<(bool Hit, string Decription)> Audit(IAuditableEntity value);
    }

    public interface ISourceExtractor
    {
        Task<(ResolutionResult ResolutionResult, string CanonicalLink)> TryResolveLink(string link);

        Task<IReadOnlyCollection<IAuditableEntity>> GetPosts(string canonicalLink);

        Task<IReadOnlyCollection<IAuditableRelation>> GetRelations(string canonicalLink);
    }


    public enum ResolutionResult
    {
        InvalidLink,
        NotSupported,
        Supported,
    }

}
