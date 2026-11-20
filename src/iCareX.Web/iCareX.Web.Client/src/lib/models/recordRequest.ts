// Keep the key after an ambiguous network failure; editing the payload creates a new command.
export type RecordCommand = {
  payload: string
  key: string
}
export function recordRequestKey(
  previous: RecordCommand | null,
  payload: unknown,
) {
  const serialized = JSON.stringify(payload)
  return previous?.payload === serialized
    ? previous
    : { payload: serialized, key: crypto.randomUUID() }
}
