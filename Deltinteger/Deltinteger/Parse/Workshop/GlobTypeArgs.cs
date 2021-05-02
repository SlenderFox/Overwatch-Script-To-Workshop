using System;
using System.Collections.Generic;

namespace Deltin.Deltinteger.Parse.Workshop
{
    /*
    This will take everything that can be provided with a type-arg and get each input type.
    Using a type-arg as a type-arg will make it pass through, for example:

    // Declare classes
    class A<T>
    class B<T> : A<T>
    // Declare variables
    A<String> aString;
    B<Number> bString;

    Doing 'Trackers[B]' will get just 'Number', while 'Trackers[A]' will get 'Number' and 'String'.

    Since a subroutine created for 'A<Number>' may not be compatible with 'A<MyStruct>', a subroutine
    will need to be generated for both.
    */
    public class GlobTypeArgCollector
    {
        public IReadOnlyDictionary<ITypeArgTrackee, ProviderTrackerInfo> Trackers => _trackers;
        Dictionary<ITypeArgTrackee, ProviderTrackerInfo> _trackers;

        public GlobTypeArgCollector(ScriptFile[] scripts)
        {
            Merge(CollectTypeArgCalls(scripts));
        }

        // Collects every call and combines them into a format that will be easy to process.
        private void Merge(Dictionary<ITypeArgTrackee, TrackeeInfo> collected)
        {
            _trackers = new Dictionary<ITypeArgTrackee, ProviderTrackerInfo>();

            // Create 
            foreach (var raw in collected)
                _trackers.Add(raw.Key, new ProviderTrackerInfo(raw.Key));

            // Add calls.
            foreach (var raw in collected)
            {
                var tracker = _trackers[raw.Key];
                foreach (var call in raw.Value.Calls)
                    for (int i = 0; i < call.TypeArgs.Length; i++)
                        call.TypeArgs[i].GenericUsage.UsedWithTypeArg(this, tracker.TypeArgs[i]);
            }
        }

        // Gets a TypeArgGlob from an AnonymousType.
        public TypeArgGlob GlobFromTypeArg(AnonymousType typeArg)
        {
            foreach (var tracker in _trackers)
                foreach (var arg in tracker.Value.TypeArgs)
                    if (typeArg == arg.Source)
                        return arg;
            
            throw new ArgumentException("The provided typeArg does not exist in the trackers.", nameof(typeArg));
        }

        private Dictionary<ITypeArgTrackee, TrackeeInfo> CollectTypeArgCalls(ScriptFile[] scripts)
        {
            var collected = new Dictionary<ITypeArgTrackee, TrackeeInfo>();

            foreach (var script in scripts)
                foreach (var typeArgCall in script.Elements.TypeArgCalls)
                {
                    // Add trackee if it does not exist.
                    if (!collected.TryGetValue(typeArgCall.Trackee, out TrackeeInfo info))
                    {
                        info = new TrackeeInfo() { Calls = new List<TrackeeCall>() };
                        collected.Add(typeArgCall.Trackee, info);
                    }

                    // Add the trackee call.
                    info.Calls.Add(new TrackeeCall() { TypeArgs = typeArgCall.TypeArgs });
                }
            
            return collected;
        }

        // The data of a trackee.
        struct TrackeeInfo
        {
            public List<TrackeeCall> Calls;
        }

        // Represents a single trackee call.
        struct TrackeeCall
        {
            public CodeType[] TypeArgs;
        }
    }

    /// <summary>Tracks the generic usage for a type provider.</summary>
    public class ProviderTrackerInfo
    {
        public TypeArgGlob[] TypeArgs { get; }

        public ProviderTrackerInfo(ITypeArgTrackee trackee)
        {
            TypeArgs = new TypeArgGlob[trackee.GenericsCount];
            for (int i = 0; i < TypeArgs.Length; i++)
                TypeArgs[i] = new TypeArgGlob(trackee.GenericTypes[i]);
        }
    }

    public class TypeArgGlob
    {
        public IReadOnlyList<CodeType> AllTypeArguments => _codeTypes;
        public AnonymousType Source { get; }
        readonly List<CodeType> _codeTypes = new List<CodeType>();
        Action<CodeType> _actions;

        public TypeArgGlob(AnonymousType source)
        {
            Source = source;
        }

        public void AddCodeType(CodeType type)
        {
            // Do not add anonymous types.
            if (type is AnonymousType) return;

            foreach (var codeType in _codeTypes)
                if (codeType.Is(type))
                    return;

            _codeTypes.Add(type);
            _actions?.Invoke(type);
        }

        // Allows a hook to run when a type arg is added to the glob.
        public void OnTypeArg(Action<CodeType> action)
        {
            foreach (var existing in _codeTypes)
                action(existing);
            
            _actions += action;
        }
    }
}