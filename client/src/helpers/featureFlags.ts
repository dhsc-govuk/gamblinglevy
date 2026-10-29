// Mirrors api/src/Application/Common/Features/Feature.cs. `/profile/me` lists a name in `features` when that flag is
// switched on in the current environment (a `Features__{Name}=true` setting on the API). Add a member here in the same
// change as the enum member, and delete it when the flag is retired — the compile errors then find every gated use.
export type FeatureFlag = 'BLUM_7637' | 'BLUM_8329' | 'BLUM_8310' | 'BLUM_9021' | 'BLUM_9087' | 'BLUM_9395';

// DAC builds the client and the API separately, so either can be ahead of the other: a name this build does not
// know is simply never asked about, and one the API does not send (or an API too old to send `features` at all)
// reads as off. Off is always the safe answer — it is what production shows until a flag is released.
export const isFeatureEnabled = (features: readonly string[] | undefined, flag: FeatureFlag): boolean => (features ?? []).includes(flag);
