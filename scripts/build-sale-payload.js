const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const templatePath = path.join(root, 'tools', 'sample-payloads', 'sale-basic.template.json');
const outPath = path.join(root, 'tools', 'sample-payloads', 'sale-basic.generated.json');

const template = JSON.parse(fs.readFileSync(templatePath, 'utf8'));
const stamp = new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);

template.Token = process.env.WEBKASSA_TOKEN || '__TOKEN_REQUIRED__';
template.ExternalCheckNumber = process.env.WEBKASSA_EXTERNAL_CHECK_NUMBER || `webkassa-smoke-sale-${stamp}`;
template.ExternalOrderNumber = process.env.WEBKASSA_EXTERNAL_ORDER_NUMBER || `webkassa-smoke-order-${stamp}`;

fs.writeFileSync(outPath, `${JSON.stringify(template, null, 2)}\n`);

console.log(`Generated ${path.relative(root, outPath)}`);
console.log(`ExternalCheckNumber=${template.ExternalCheckNumber}`);
if (template.Token === '__TOKEN_REQUIRED__') {
  console.log('Token placeholder kept; set WEBKASSA_TOKEN only when executing an approved live smoke.');
}
