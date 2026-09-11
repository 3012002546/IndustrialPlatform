const challengeMessageType = 'industrial-embedded-handshake.challenge'
const assertionMessageType = 'industrial-embedded-handshake.assertion'

function exactOrigin(value) {
  const origin = new URL(value).origin
  if (origin !== value) throw new Error('An exact origin is required.')
  return origin
}

async function getChallenge(hostOrigin) {
  const response = await fetch(`${hostOrigin}/api/v1/embedded/challenges`, {
    method: 'POST',
    credentials: 'include',
  })
  if (!response.ok) throw new Error('The embedded handshake challenge was rejected.')
  return response.json()
}

async function getAssertion(hostOrigin, nonce) {
  const response = await fetch(`${hostOrigin}/api/v1/embedded/assertions?nonce=${encodeURIComponent(nonce)}`, {
    credentials: 'include',
  })
  if (!response.ok) throw new Error('The upstream host did not provide an authenticated assertion.')
  return response.json()
}

/**
 * Establishes a Collaboration session from the upstream host's authenticated
 * server session. The browser never supplies an external subject, tenant, or
 * platform user; it only carries the HttpOnly browser-binding cookie.
 */
export async function establishEmbeddedSession({ hostUrl, frame, targetOrigin }) {
  if (!(frame instanceof HTMLIFrameElement) || frame.contentWindow === null)
    throw new Error('A live Collaboration iframe is required.')
  const hostOrigin = exactOrigin(hostUrl)
  const parentOrigin = exactOrigin(targetOrigin)
  const challenge = await getChallenge(hostOrigin)

  frame.contentWindow.postMessage(
    {
      type: challengeMessageType,
      challenge: challenge.challenge,
      nonce: challenge.nonce,
      expiresOn: challenge.expiresOn,
    },
    parentOrigin,
  )

  const assertion = await getAssertion(hostOrigin, challenge.nonce)
  frame.contentWindow.postMessage(
    {
      type: assertionMessageType,
      assertion: assertion.assertion,
      nonce: challenge.nonce,
    },
    parentOrigin,
  )

  const response = await fetch(`${hostOrigin}/api/v1/embedded/exchanges`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ assertion: assertion.assertion }),
  })
  if (!response.ok) throw new Error('The embedded handshake exchange was rejected.')
  return response.json()
}

/** Renews a session with a fresh challenge and a fresh upstream assertion. */
export async function renewEmbeddedSession({ hostUrl, frame, targetOrigin }) {
  const hostOrigin = exactOrigin(hostUrl)
  const parentOrigin = exactOrigin(targetOrigin)
  const challenge = await getChallenge(hostOrigin)
  const assertion = await getAssertion(hostOrigin, challenge.nonce)
  if (frame instanceof HTMLIFrameElement && frame.contentWindow !== null) {
    frame.contentWindow.postMessage(
      {
        type: challengeMessageType,
        challenge: challenge.challenge,
        nonce: challenge.nonce,
        expiresOn: challenge.expiresOn,
      },
      parentOrigin,
    )
    frame.contentWindow.postMessage(
      {
        type: assertionMessageType,
        assertion: assertion.assertion,
        nonce: challenge.nonce,
      },
      parentOrigin,
    )
  }

  const response = await fetch(`${hostOrigin}/api/v1/embedded/session/renew`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ assertion: assertion.assertion }),
  })
  if (!response.ok) throw new Error('The embedded session renewal was rejected.')
  return response.json()
}
