#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices;

// Mirrors SPTQuestMap.Core's own polyfill of the same name. Core's copy is internal to that
// assembly and this project cannot see it, and record types (used by CoopListView) need this
// type to exist somewhere the compiler can find it on netstandard2.1.
internal static class IsExternalInit;
#endif
