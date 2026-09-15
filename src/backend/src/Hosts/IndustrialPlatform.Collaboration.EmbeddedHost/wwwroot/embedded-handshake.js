const challengeMessageType = 'industrial-embedded-handshake.challenge'
const assertionMessageType = 'industrial-embedded-handshake.assertion'

function exactOrigin(value) {
  const origin = new URL(value).origin
  if (origin !== value) throw new Error('An exact origin is required.')
  return origin
}

function withAccount(url, account) {
  if (account === undefined) return url
  if (typeof account !== 'string' || account.length === 0 || account.trim() !== account)
    throw new Error('A single non-empty account is required.')
  const separator = url.includes('?') ? '&' : '?'
  return `${url}${separator}account=${encodeURIComponent(account)}`
}

async function getChallenge(hostOrigin, account) {
  const response = await fetch(withAccount(`${hostOrigin}/api/v1/embedded/challenges`, account), {
    method: 'POST',
    credentials: 'include',
  })
  if (!response.ok) throw new Error('The embedded handshake challenge was rejected.')
  return response.json()
}

async function getAssertion(hostOrigin, nonce, account) {
  const response = await fetch(withAccount(`${hostOrigin}/api/v1/embedded/assertions?nonce=${encodeURIComponent(nonce)}`, account), {
    credentials: 'include',
  })
  if (!response.ok) throw new Error('The upstream host did not provide an authenticated assertion.')
  return response.json()
}

/**
 * 根据上游宿主已认证的服务端会话建立协作会话。
 * 浏览器只携带用于绑定浏览器的 HttpOnly Cookie，
 * 不能自行提供外部用户、租户或平台用户身份。
 */
export async function establishEmbeddedSession({ hostUrl, frame, targetOrigin, account }) {
  if (!(frame instanceof HTMLIFrameElement) || frame.contentWindow === null)
    throw new Error('A live Collaboration iframe is required.')
  const hostOrigin = exactOrigin(hostUrl)
  const parentOrigin = exactOrigin(targetOrigin)
  const challenge = await getChallenge(hostOrigin, account)

  frame.contentWindow.postMessage(
    {
      type: challengeMessageType,
      challenge: challenge.challenge,
      nonce: challenge.nonce,
      expiresOn: challenge.expiresOn,
    },
    parentOrigin,
  )

  const assertion = await getAssertion(hostOrigin, challenge.nonce, account)
  frame.contentWindow.postMessage(
    {
      type: assertionMessageType,
      assertion: assertion.assertion,
      nonce: challenge.nonce,
    },
    parentOrigin,
  )

  const response = await fetch(withAccount(`${hostOrigin}/api/v1/embedded/exchanges`, account), {
    method: 'POST',
    credentials: 'include',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ assertion: assertion.assertion, ...(account === undefined ? {} : { account }) }),
  })
  if (!response.ok) throw new Error('The embedded handshake exchange was rejected.')
  return response.json()
}

/** 使用新的挑战和上游身份断言续期会话。 */
export async function renewEmbeddedSession({ hostUrl, frame, targetOrigin, account }) {
  const hostOrigin = exactOrigin(hostUrl)
  const parentOrigin = exactOrigin(targetOrigin)
  const challenge = await getChallenge(hostOrigin, account)
  const assertion = await getAssertion(hostOrigin, challenge.nonce, account)
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

  const response = await fetch(withAccount(`${hostOrigin}/api/v1/embedded/session/renew`, account), {
    method: 'POST',
    credentials: 'include',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ assertion: assertion.assertion, ...(account === undefined ? {} : { account }) }),
  })
  if (!response.ok) throw new Error('The embedded session renewal was rejected.')
  return response.json()
}
