import Script from "next/script";

/** The university event registration forms are hosted by LeadConnector (GoHighLevel); the old pages embedded them exactly like this. */
export default function LeadConnectorForm({ formId, name, height }: { formId: string; name: string; height: number }) {
  return (
    <>
      <iframe
        src={`https://api.leadconnectorhq.com/widget/form/${formId}`}
        title={name}
        data-layout="{'id':'INLINE'}"
        data-trigger-type="alwaysShow"
        data-activation-type="alwaysActivated"
        data-deactivation-type="neverDeactivate"
        data-form-name={name}
        data-height={height}
        data-layout-iframe-id={`inline-${formId}`}
        data-form-id={formId}
        id={`inline-${formId}`}
        style={{ width: "100%", height: `${height}px`, border: "none", borderRadius: "3px" }}
      />
      <Script src="https://link.msgsndr.com/js/form_embed.js" strategy="lazyOnload" />
    </>
  );
}
