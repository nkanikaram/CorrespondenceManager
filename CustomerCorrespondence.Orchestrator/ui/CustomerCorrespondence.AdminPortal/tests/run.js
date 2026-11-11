/* Runs every unit test in plain Node: `npm test`. */
'use strict';

var h = require('./harness');

require('./grid.service.test');
require('./grid-filter.service.test');
require('./pagination.test');
require('./auth-and-filters.test');
require('./services.test');

h.summary();
