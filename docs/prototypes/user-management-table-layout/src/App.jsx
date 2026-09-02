import { Fragment, useMemo, useRef, useState } from 'react';
import {
  ApartmentOutlined, CalendarOutlined, ClearOutlined, ColumnWidthOutlined, CompressOutlined,
  DownloadOutlined, ExpandOutlined, FilterFilled, FilterOutlined, PrinterOutlined,
  ReloadOutlined, SearchOutlined, SettingOutlined, SortAscendingOutlined,
  SortDescendingOutlined, VerticalAlignMiddleOutlined,
} from '@ant-design/icons';

const rows = [
  ['admin', '平台管理员', '启用', '否', 'admin@industrial.local', '138****1024', 3, '2026-08-28 09:42', '2026-01-08 11:20'],
  ['zhangwei', '张伟', '启用', '否', 'zhangwei@industrial.local', '139****5271', 2, '2026-08-28 08:36', '2026-03-12 14:18'],
  ['liuna', '刘娜', '启用', '是', 'liuna@industrial.local', '136****8043', 1, '2026-08-27 17:51', '2026-04-06 09:06'],
  ['chenhao', '陈浩', '停用', '否', 'chenhao@industrial.local', '135****3108', 1, '2026-08-22 15:14', '2026-04-18 16:34'],
  ['wangfang', '王芳', '启用', '否', 'wangfang@industrial.local', '137****6680', 2, '2026-08-28 07:58', '2026-05-21 10:15'],
  ['zhaolei', '赵磊', '启用', '是', 'zhaolei@industrial.local', '188****9042', 1, '2026-08-26 13:27', '2026-06-03 13:48'],
  ['sunli', '孙丽', '启用', '否', 'sunli@industrial.local', '186****2297', 2, '2026-08-25 10:09', '2026-06-25 08:30'],
  ['zhouyang', '周洋', '启用', '否', 'zhouyang@industrial.local', '158****7624', 1, '2026-08-27 16:42', '2026-07-02 11:52'],
];

const columns = [
  { key: 'loginName', label: '登录名', type: 'text', width: 132 },
  { key: 'name', label: '姓名', type: 'text', width: 112 },
  { key: 'status', label: '状态', type: 'select', options: ['启用', '停用'], width: 92 },
  { key: 'mustChangePassword', label: '改密', type: 'select', options: ['是', '否'], width: 82 },
  { key: 'email', label: '邮箱', type: 'text', width: 190 },
  { key: 'phone', label: '手机号', type: 'text', width: 126 },
  { key: 'effectiveRoleCount', label: '有效角色', type: 'number', width: 100 },
  { key: 'lastLoginOn', label: '最近登录', type: 'date', width: 178 },
  { key: 'createdOn', label: '创建时间', type: 'date', width: 178 },
];

const values = (row) => Object.fromEntries(columns.map((column, index) => [column.key, row[index]]));
const emptyFilters = () => Object.fromEntries(columns.map((column) => [column.key, column.type === 'date' ? ['', ''] : '']));

function IconButton({ label, active, children, onClick, disabled }) {
  return <button className={`icon-button ${active ? 'is-active' : ''}`} aria-label={label} title={label} onClick={onClick} disabled={disabled}>{children}</button>;
}

function Popover({ open, children, align = 'left' }) {
  return open ? <div className={`popover popover-${align}`}>{children}</div> : null;
}

function DateRangeFilter({ value, onChange }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="date-filter">
      <button className="date-trigger" onClick={() => setOpen(!open)} title="选择日期区间">
        <span>{value[0] || '开始日期'}</span><span className="range-dash">—</span><span>{value[1] || '结束日期'}</span><CalendarOutlined />
      </button>
      {open && (
        <div className="date-panel">
          <strong>日期区间</strong>
          <label>开始日期<input type="date" value={value[0]} onChange={(e) => onChange([e.target.value, value[1]])} /></label>
          <label>结束日期<input type="date" value={value[1]} onChange={(e) => onChange([value[0], e.target.value])} /></label>
          <div className="date-actions"><button onClick={() => onChange(['', ''])}>清空</button><button className="primary" onClick={() => setOpen(false)}>确定</button></div>
        </div>
      )}
    </div>
  );
}

export function App() {
  const tableShell = useRef(null);
  const [filterMode, setFilterMode] = useState(false);
  const [quickSearch, setQuickSearch] = useState('');
  const [filters, setFilters] = useState(emptyFilters);
  const [sort, setSort] = useState({ key: 'createdOn', direction: 'desc' });
  const [openMenu, setOpenMenu] = useState('');
  const [density, setDensity] = useState('default');
  const [visible, setVisible] = useState(Object.fromEntries(columns.map((column) => [column.key, true])));
  const [refreshing, setRefreshing] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState(() => new Set());
  const [groupFields, setGroupFields] = useState([]);
  const shownColumns = columns.filter((column) => visible[column.key]);

  const data = useMemo(() => {
    const query = quickSearch.trim().toLowerCase();
    return rows.map(values).filter((row) => {
      if (!filterMode && query) return Object.values(row).some((value) => String(value).toLowerCase().includes(query));
      if (!filterMode) return true;
      return columns.every((column) => {
        const filter = filters[column.key];
        if (column.type === 'date') {
          const date = String(row[column.key]).slice(0, 10);
          return (!filter[0] || date >= filter[0]) && (!filter[1] || date <= filter[1]);
        }
        return !filter || String(row[column.key]).toLowerCase().includes(String(filter).toLowerCase());
      });
    }).sort((a, b) => {
      for (const field of groupFields) {
        const grouped = String(a[field]).localeCompare(String(b[field]), 'zh-CN', { numeric: true });
        if (grouped) return grouped;
      }
      const result = String(a[sort.key]).localeCompare(String(b[sort.key]), 'zh-CN', { numeric: true });
      return sort.direction === 'asc' ? result : -result;
    });
  }, [filterMode, filters, groupFields, quickSearch, sort]);

  const changeFilter = (key, value) => setFilters((current) => ({ ...current, [key]: value }));
  const clearQueries = () => { setQuickSearch(''); setFilters(emptyFilters()); setPage(1); };
  const toggleSort = (key) => setSort((current) => ({ key, direction: current.key === key && current.direction === 'asc' ? 'desc' : 'asc' }));
  const refresh = () => { setRefreshing(true); window.setTimeout(() => setRefreshing(false), 650); };
  const toggleFullscreen = () => document.fullscreenElement ? document.exitFullscreen() : tableShell.current?.requestFullscreen();
  const selectedUsers = rows.map(values).filter((row) => selected.has(row.loginName));
  const toggleRow = (loginName) => setSelected((current) => {
    const next = new Set(current);
    next.has(loginName) ? next.delete(loginName) : next.add(loginName);
    return next;
  });
  const toggleAll = () => setSelected((current) => {
    const visibleKeys = data.map((row) => row.loginName);
    const allSelected = visibleKeys.length > 0 && visibleKeys.every((key) => current.has(key));
    const next = new Set(current);
    visibleKeys.forEach((key) => allSelected ? next.delete(key) : next.add(key));
    return next;
  });
  const toggleGroup = (key) => setGroupFields((current) => current.includes(key) ? current.filter((field) => field !== key) : [...current, key]);

  return (
    <main className="page-shell" onClick={(event) => { if (!event.target.closest('.menu-anchor') && !event.target.closest('.date-filter')) setOpenMenu(''); }}>
      <header className="page-heading">
        <div><p className="eyebrow">身份与访问管理</p><h1>用户管理</h1><p>维护平台用户、登录状态和角色授权关系。</p></div>
        <span className="record-summary">共 {rows.length} 名用户</span>
      </header>

      <section className="business-actions" aria-label="业务操作">
        <button className="primary">新增用户</button><button>批量启用</button><button>批量停用</button><button>导入</button>
      </section>

      <section className={`table-shell density-${density}`} ref={tableShell}>
        <div className="table-toolbar">
          <div className="toolbar-group left-tools">
            <IconButton label={filterMode ? '切换到顶部快速搜索' : '切换到列头查询'} active={filterMode} onClick={() => { setFilterMode(!filterMode); setQuickSearch(''); setFilters(emptyFilters()); }}>{filterMode ? <FilterFilled /> : <FilterOutlined />}</IconButton>
            <div className="menu-anchor">
              <IconButton label="排序" active={openMenu === 'sort'} onClick={() => setOpenMenu(openMenu === 'sort' ? '' : 'sort')}><SortAscendingOutlined /></IconButton>
              <Popover open={openMenu === 'sort'}><div className="popover-title">排序依据</div>{columns.filter((column) => ['loginName', 'name', 'lastLoginOn', 'createdOn'].includes(column.key)).map((column) => <button className="menu-row" key={column.key} onClick={() => { toggleSort(column.key); setOpenMenu(''); }}><span>{column.label}</span>{sort.key === column.key && (sort.direction === 'asc' ? <SortAscendingOutlined /> : <SortDescendingOutlined />)}</button>)}</Popover>
            </div>
            <div className="menu-anchor">
              <IconButton label="分组" active={openMenu === 'group' || groupFields.length > 0} onClick={() => setOpenMenu(openMenu === 'group' ? '' : 'group')}><ApartmentOutlined /></IconButton>
              <Popover open={openMenu === 'group'}>
                <div className="popover-title">分组字段（按勾选顺序）</div>
                {columns.map((column) => <label className="check-row" key={column.key}><input type="checkbox" checked={groupFields.includes(column.key)} onChange={() => toggleGroup(column.key)} /><span className="check-label">{column.label}</span>{groupFields.includes(column.key) && <span className="group-order">{groupFields.indexOf(column.key) + 1}</span>}</label>)}
                <button className="popover-action" disabled={!groupFields.length} onClick={() => setGroupFields([])}>清空分组</button>
              </Popover>
            </div>
            <label className={`quick-search ${filterMode ? 'is-disabled' : ''}`}><SearchOutlined /><input value={quickSearch} onChange={(e) => setQuickSearch(e.target.value)} disabled={filterMode} placeholder={filterMode ? '当前使用列头查询' : '快速搜索当前数据'} /></label>
            <div className="menu-anchor">
              <IconButton label="下载" active={openMenu === 'download'} onClick={() => setOpenMenu(openMenu === 'download' ? '' : 'download')}><DownloadOutlined /></IconButton>
              <Popover open={openMenu === 'download'}><div className="popover-title">下载数据</div>{['CSV', 'HTML', 'XML', 'TXT', 'Excel（自定义）'].map((type) => <button className="menu-row" key={type}>{type}</button>)}</Popover>
            </div>
            <IconButton label="打印" onClick={() => window.print()}><PrinterOutlined /></IconButton>
          </div>

          <div className="toolbar-group right-tools">
            <IconButton label="清空查询" onClick={clearQueries}><ClearOutlined /></IconButton>
            <IconButton label="刷新表格" onClick={refresh}><ReloadOutlined className={refreshing ? 'is-spinning' : ''} /></IconButton>
            <IconButton label="表格全屏" onClick={toggleFullscreen}>{document.fullscreenElement ? <CompressOutlined /> : <ExpandOutlined />}</IconButton>
            <div className="menu-anchor">
              <IconButton label="列设置" active={openMenu === 'columns'} onClick={() => setOpenMenu(openMenu === 'columns' ? '' : 'columns')}><SettingOutlined /></IconButton>
              <Popover open={openMenu === 'columns'} align="right"><div className="popover-title">列设置</div>{columns.map((column) => <label className="check-row" key={column.key}><input type="checkbox" checked={visible[column.key]} onChange={() => setVisible((current) => ({ ...current, [column.key]: !current[column.key] }))} /><span>{column.label}</span><ColumnWidthOutlined /></label>)}</Popover>
            </div>
            <div className="menu-anchor">
              <IconButton label="行设置" active={openMenu === 'density'} onClick={() => setOpenMenu(openMenu === 'density' ? '' : 'density')}><VerticalAlignMiddleOutlined /></IconButton>
              <Popover open={openMenu === 'density'} align="right"><div className="popover-title">行高</div>{[['default', '默认'], ['medium', '中等'], ['compact', '紧凑']].map(([key, label]) => <button className={`menu-row ${density === key ? 'selected' : ''}`} key={key} onClick={() => { setDensity(key); setOpenMenu(''); }}>{label}</button>)}</Popover>
            </div>
          </div>
        </div>

        <div className="table-scroll">
          <table>
            <colgroup><col className="selection-col" /><col className="index-col" />{shownColumns.map((column) => <col key={column.key} style={{ width: column.width }} />)}<col className="action-col" /></colgroup>
            <thead>
              <tr className="title-row"><th><input type="checkbox" aria-label="全选" checked={data.length > 0 && data.every((row) => selected.has(row.loginName))} onChange={toggleAll} /></th><th>序号</th>{shownColumns.map((column) => <th key={column.key}><button className={`column-title ${sort.key === column.key ? 'is-sorted' : ''}`} onClick={() => toggleSort(column.key)}><span>{column.label}</span><span className="sort-pair"><SortAscendingOutlined /><SortDescendingOutlined /></span></button></th>)}<th>操作</th></tr>
              {filterMode && <tr className="filter-row"><th></th><th></th>{shownColumns.map((column) => <th key={column.key}>{column.type === 'select' ? <select value={filters[column.key]} onChange={(e) => changeFilter(column.key, e.target.value)}><option value="">全部</option>{column.options.map((option) => <option key={option}>{option}</option>)}</select> : column.type === 'date' ? <DateRangeFilter value={filters[column.key]} onChange={(value) => changeFilter(column.key, value)} /> : <label className="header-input"><input type={column.type} value={filters[column.key]} onChange={(e) => changeFilter(column.key, e.target.value)} placeholder={column.type === 'number' ? '输入数值' : '输入关键字'} /><SearchOutlined /></label>}</th>)}<th></th></tr>}
            </thead>
            <tbody>{data.map((row, index) => <Fragment key={row.loginName}>{groupFields.map((field, level) => {
              const changed = index === 0 || groupFields.slice(0, level + 1).some((key) => data[index - 1][key] !== row[key]);
              const column = columns.find((item) => item.key === field);
              return changed ? <tr className="group-row" key={`${row.loginName}-${field}`}><td colSpan={shownColumns.length + 3}><span style={{ paddingLeft: level * 18 }}>{column.label}：{row[field]}</span></td></tr> : null;
            })}<tr className={selected.has(row.loginName) ? 'is-selected' : ''}><td><input type="checkbox" aria-label={`选择 ${row.name}`} checked={selected.has(row.loginName)} onChange={() => toggleRow(row.loginName)} /></td><td>{index + 1}</td>{shownColumns.map((column) => <td key={column.key}>{column.key === 'status' ? <span className={`status ${row.status === '启用' ? 'enabled' : 'disabled'}`}>{row.status}</span> : column.key === 'mustChangePassword' ? <span className={row.mustChangePassword === '是' ? 'warning' : ''}>{row.mustChangePassword}</span> : row[column.key]}</td>)}<td><button className="text-action">编辑</button><button className="text-action danger">停用</button></td></tr></Fragment>)}</tbody>
          </table>
          {!data.length && <div className="empty-state">没有符合条件的用户</div>}
        </div>

        <footer className="table-footer">
          <div className="selection-summary" aria-live="polite"><strong>已选择 {selectedUsers.length} 行</strong><button disabled={!selectedUsers.length} onClick={() => setSelected(new Set())}>清空选择</button></div>
          <div className="pagination"><span>共 {data.length} 条数据</span><button disabled={page === 1} onClick={() => setPage(Math.max(1, page - 1))}>‹</button>{[1, 2, 3, 4, 5].map((value) => <button key={value} className={page === value ? 'current' : ''} onClick={() => setPage(value)}>{value}</button>)}<button onClick={() => setPage(Math.min(5, page + 1))}>›</button><select defaultValue="25 条/页">{[10,25,50,100,150,200].map((size) => <option key={size}>{size} 条/页</option>)}</select><label>跳至 <input type="number" min="1" max="5" defaultValue="1" /> 页</label></div>
        </footer>
      </section>
    </main>
  );
}
