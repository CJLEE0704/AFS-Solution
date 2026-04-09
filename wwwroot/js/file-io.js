(function(global){
  function parseDelimited(text, projName){
    const lines=text.split(/\r?\n/).map(x=>x.trim()).filter(Boolean);
    const rows=[];
    for(const ln of lines){
      const c=ln.split(/[;,\t]/).map(v=>v.trim());
      if(c.length<2) continue;
      rows.push({
        pipeId:c[1]||c[0],
        pipeName:c[1]||c[0],
        size:parseInt(c[2]||'32',10)||32,
        totalLength:parseInt(c[3]||'0',10)||0,
        material:c[4]||'SS400',
        status:'미완료',
        projName:projName
      });
    }
    return rows;
  }

  const orig=global.handleAddFileToProject;
  global.handleAddFileToProject=async function(ev){
    const files=[...(ev?.target?.files||[])];
    for(const f of files){
      const ext=(f.name.split('.').pop()||'').toLowerCase();
      if(ext==='xlsx'){
        alert('XLSX 파서는 SheetJS 경로로 확장 가능하도록 분리되었습니다. 현재 빌드에는 CSV/TXT 즉시 반영만 활성화되어 있습니다.');
        continue;
      }
      if(ext==='csv'||ext==='txt'){
        const txt=await f.text();
        const projectName=f.name.replace(/\.[^.]+$/,'');
        const rows=parseDelimited(txt,projectName);
        const projectId='proj_'+Date.now()+'_'+Math.floor(Math.random()*1000);
        global.sendToCSharp?.('SAVE_PROJECT_PIPES','ALL',JSON.stringify({
          projectId, projectName, fileType:ext.toUpperCase(), addedAt:new Date().toISOString(), pipes:rows
        }));
      }
    }
    return orig?.apply(this,arguments);
  };
})(window);
